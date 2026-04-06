using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Tag;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._NC.FirstPerson.Renderers;

/// <summary>
/// Рендерер стен методом DDA Raycasting с текстурированием.
/// Пускает лучи по вертикальным колонкам экрана,
/// определяет столкновения с тайлами-стенами
/// и рисует вертикальные срезы текстур стен с учетом глубины.
/// </summary>
public sealed class FirstPersonGeometryRenderer
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private readonly TagSystem _tagSystem;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;

    // Кэш текстур стен: EntityUid → Texture (Point-filtered, без сглаживания)
    private readonly Dictionary<EntityUid, Texture> _wallTextureCache = new();

    // Цвет затемнения для боковых граней стен (side == 1)
    private static readonly Color ShadowModulate = new(0.6f, 0.6f, 0.6f);

    // Высота стены в юнитах мира (1 тайл = 1 юнит).
    // Увеличиваем, чтобы стены выглядели «выше» и заполняли экран от пола до потолка.
    private const float WallHeight = 1.0f;

    public FirstPersonGeometryRenderer()
    {
        IoCManager.InjectDependencies(this);
        _tagSystem = _entityManager.System<TagSystem>();
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
    }

    /// <summary>
    /// Основной метод DDA Raycasting.
    /// Для каждой вертикальной колонки экрана пускает луч,
    /// находит стену, вычисляет UV и рисует текстурированный срез.
    /// </summary>
    public void RenderDDA(
        DrawingHandleScreen handle,
        MapCoordinates mapPos,
        Angle rotation,
        float fov,
        Vector2 screenSize,
        float[] zBuffer,
        int stepPixel = 2)
    {
        if (!_mapManager.TryFindGridAt(mapPos, out var gridUid, out var grid))
            return;

        // Преобразуем мировые координаты в локальные координаты грида
        var invMatrix = _transformSystem.GetInvWorldMatrix(gridUid);
        var localPos = Vector2.Transform(mapPos.Position, invMatrix);
        var localRot = rotation - _transformSystem.GetWorldRotation(gridUid);

        int screenWidth = (int)screenSize.X;
        int screenHeight = (int)screenSize.Y;

        var playerPos = localPos;
        var dir = localRot.ToVec();

        // Длина вектора плоскости камеры (определяет FOV)
        float planeLength = (float)Math.Tan(MathHelper.DegreesToRadians(fov) / 2.0);
        var plane = new Vector2(-dir.Y, dir.X) * planeLength;

        // Вычисляем вертикальный масштаб проекции.
        // В классическом рейкастинге (Wolfenstein3D) используется формула:
        //   lineHeight = screenHeight / perpWallDist
        // Это работает для квадратного экрана (aspect ~1:1).
        // Для широких экранов нужно учитывать, что вертикальный FOV
        // зависит от горизонтального FOV и aspect ratio.
        //
        // verticalFov = 2 * atan(tan(hFov/2) / aspectRatio)
        // distProjectionPlane = (screenHeight / 2) / tan(vFov / 2)
        //
        // Но проще считать: при perpWallDist=1, стена должна занимать
        // screenHeight * (wallHeight / (2 * tan(vFov/2))) / perpWallDist пикселей.
        //
        // Упрощённо: projScale = screenHeight / (2 * planeLength / aspectRatio)
        // Так как planeLength = tan(hFov/2), а vPlaneLength = planeLength / aspect:
        //   projScale = screenHeight / (2 * planeLength / aspect) = screenHeight * aspect / (2 * planeLength)
        //   но это не нужно — формула lineHeight = screenHeight / perpWallDist уже верна для
        //   стен высотой 1 юнит, если вертикальный FOV согласован.
        //
        // Итоговый множитель: чтобы стена высотой WallHeight при расстоянии perpWallDist
        // занимала правильную долю экрана, используем:
        //   lineHeight = (screenHeight * WallHeight) / (perpWallDist * verticalPlane)
        //   где verticalPlane = planeLength / aspectRatio = planeLength * screenHeight / screenWidth
        //
        // => lineHeight = screenWidth / (perpWallDist * planeLength)
        // ...но это тоже неинтуитивно. Давайте просто подберём правильную формулу.
        //
        // Правильная формула для рейкастинга:
        //   lineProjHeight = wallWorldHeight * distToProjectionPlane / perpWallDist
        //   distToProjectionPlane = (screenWidth / 2) / tan(hFov / 2) = screenWidth / (2 * planeLength)
        //
        // Итого:
        //   lineHeight = WallHeight * screenWidth / (2 * planeLength * perpWallDist)

        float distToProj = screenWidth / (2.0f * planeLength);

        // --- DDA Raycast цикл по колонкам экрана ---
        for (int x = 0; x < screenWidth; x += stepPixel)
        {
            // Нормализованная X-координата камеры: -1 (лево) .. +1 (право)
            float cameraX = 2 * x / (float)screenWidth - 1;
            float rayDirX = dir.X + plane.X * cameraX;
            float rayDirY = dir.Y + plane.Y * cameraX;

            // Текущая ячейка сетки (тайл), в которой находится луч
            int mapX = (int)Math.Floor(playerPos.X);
            int mapY = (int)Math.Floor(playerPos.Y);

            float sideDistX;
            float sideDistY;

            // Дельты DDA (расстояние, которое луч проходит через одну ячейку)
            float deltaDistX = (rayDirX == 0) ? float.MaxValue : Math.Abs(1 / rayDirX);
            float deltaDistY = (rayDirY == 0) ? float.MaxValue : Math.Abs(1 / rayDirY);

            float perpWallDist = float.MaxValue;

            int stepX;
            int stepY;

            bool hit = false;
            int side = 0; // 0 = удар по оси X (NS-грань), 1 = удар по оси Y (WE-грань)
            EntityUid hitWallUid = EntityUid.Invalid; // uid стены, в которую попал луч

            // Определяем шаг и начальное расстояние для оси X
            if (rayDirX < 0)
            {
                stepX = -1;
                sideDistX = (playerPos.X - mapX) * deltaDistX;
            }
            else
            {
                stepX = 1;
                sideDistX = (mapX + 1.0f - playerPos.X) * deltaDistX;
            }

            // Определяем шаг и начальное расстояние для оси Y
            if (rayDirY < 0)
            {
                stepY = -1;
                sideDistY = (playerPos.Y - mapY) * deltaDistY;
            }
            else
            {
                stepY = 1;
                sideDistY = (mapY + 1.0f - playerPos.Y) * deltaDistY;
            }

            int hitDistance = 0;
            const int maxDepth = 64; // Ограничение дальности рейкаста (тайлов)

            // --- DDA шаг: продвигаем луч по сетке ---
            while (!hit && hitDistance < maxDepth)
            {
                if (sideDistX < sideDistY)
                {
                    sideDistX += deltaDistX;
                    mapX += stepX;
                    side = 0;
                }
                else
                {
                    sideDistY += deltaDistY;
                    mapY += stepY;
                    side = 1;
                }

                // Проверка тайла карты на наличие стены
                var indices = new Vector2i(mapX, mapY);
                var anchored = _mapSystem.GetAnchoredEntities(gridUid, grid, indices);

                foreach (var uid in anchored)
                {
                    // Ищем строго сущность-стену (по тегу Wall)
                    if (_tagSystem.HasTag(uid, "Wall"))
                    {
                        hit = true;
                        hitWallUid = uid;
                        break;
                    }
                }
                hitDistance++;
            }

            if (!hit)
                continue;

            // --- Вычисление перпендикулярной дистанции (коррекция "рыбьего глаза") ---
            if (side == 0)
                perpWallDist = (mapX - playerPos.X + (1 - stepX) / 2.0f) / rayDirX;
            else
                perpWallDist = (mapY - playerPos.Y + (1 - stepY) / 2.0f) / rayDirY;

            // Защита от деления на ноль
            if (perpWallDist < 0.001f)
                perpWallDist = 0.001f;

            // Запись дистанции в буфер глубины (для ширины колонки = stepPixel)
            for (int i = 0; i < stepPixel && (x + i) < screenWidth; i++)
            {
                zBuffer[x + i] = perpWallDist;
            }

            // --- Высота стены на экране ---
            // Правильная формула: высота в пикселях = (высота стены в мире) * distToProj / perpWallDist
            int lineHeight = (int)(WallHeight * distToProj / perpWallDist);

            int drawStart = -lineHeight / 2 + screenHeight / 2;
            if (drawStart < 0)
                drawStart = 0;

            int drawEnd = lineHeight / 2 + screenHeight / 2;
            if (drawEnd >= screenHeight)
                drawEnd = screenHeight - 1;

            // --- Вычисление UV-координаты X (wallX) ---
            float wallX;
            if (side == 0)
                wallX = playerPos.Y + perpWallDist * rayDirY;
            else
                wallX = playerPos.X + perpWallDist * rayDirX;

            // Нормализация: оставляем дробную часть (0.0 .. 1.0)
            wallX -= (float)Math.Floor(wallX);

            // Инверсия текстуры для определенных направлений,
            // чтобы стены не были зеркально отражены
            if (side == 0 && rayDirX > 0)
                wallX = 1.0f - wallX;
            if (side == 1 && rayDirY < 0)
                wallX = 1.0f - wallX;

            // --- Получение текстуры стены ---
            var wallTexture = GetWallTexture(hitWallUid);

            if (wallTexture != null)
            {
                // Вычисляем X-координату в текстуре (пиксель)
                int texX = (int)(wallX * wallTexture.Width);
                if (texX < 0) texX = 0;
                if (texX >= wallTexture.Width) texX = wallTexture.Width - 1;

                // Область на экране, куда рисуем эту полоску стены
                var screenRect = new UIBox2(x, drawStart, x + stepPixel, drawEnd);

                // Область текстуры: 1 пиксель ширины, вся высота
                var subRegion = new UIBox2(texX, 0, texX + 1, wallTexture.Height);

                // Затемнение для боковых граней (side==1) — создаёт иллюзию объёма
                Color? modulate = side == 1 ? ShadowModulate : null;

                handle.DrawTextureRectRegion(wallTexture, screenRect, subRegion, modulate);
            }
            else
            {
                // Фоллбек: если текстуру не удалось получить — рисуем сплошным цветом
                Color color = side == 0 ? new Color(180, 180, 180) : new Color(100, 100, 100);
                var box = new UIBox2(x, drawStart, x + stepPixel, drawEnd);
                handle.DrawRect(box, color);
            }
        }
    }

    /// <summary>
    /// Извлекает текстуру стены из SpriteComponent сущности.
    /// Кэширует результат, чтобы не дергать RSI каждый кадр.
    /// Использует первый видимый слой с валидным состоянием.
    /// </summary>
    private Texture? GetWallTexture(EntityUid uid)
    {
        if (!uid.Valid)
            return null;

        // Проверяем кэш
        if (_wallTextureCache.TryGetValue(uid, out var cached))
            return cached;

        // Пытаемся извлечь текстуру из SpriteComponent стены
        if (!_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
            return null;

        Texture? texture = null;

        // Проходим по слоям спрайта, берем первый видимый с текстурой
        foreach (var layer in sprite.AllLayers)
        {
            if (!layer.Visible)
                continue;

            // Приоритет 1: прямая текстура на слое
            if (layer.Texture != null)
            {
                texture = layer.Texture;
                break;
            }

            // Приоритет 2: текстура из RSI-стейта (Default кадр)
            if (layer.RsiState.IsValid)
            {
                var rsi = layer.Rsi ?? sprite.BaseRSI;
                if (rsi != null && rsi.TryGetState(layer.RsiState, out var state))
                {
                    // Берём Default-направление, первый кадр
                    texture = state.GetFrame(RsiDirection.South, 0);
                    break;
                }
            }
        }

        // Фоллбек: Icon из SpriteComponent
        texture ??= sprite.Icon?.Default;

        // Кэшируем результат (даже null кэшировать не будем, чтобы повторно пробовать)
        if (texture != null)
            _wallTextureCache[uid] = texture;

        return texture;
    }
}
