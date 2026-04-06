using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Client.Player;
using Content.Shared.Tag;
using System;
using System.Numerics;
using System.Collections.Generic;

namespace Content.Client._NC.FirstPerson.Renderers;

public sealed class FirstPersonSpriteRenderer
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;

    private readonly SharedTransformSystem _transformSystem;
    private readonly Dictionary<EntityUid, IRenderTexture> _cachedTextures = new();

    private struct SpriteDrawCommand
    {
        public EntityUid Uid;
        public Texture Texture;
        public float TransformX;
        public float TransformY;
    }

    public FirstPersonSpriteRenderer()
    {
        IoCManager.InjectDependencies(this);
        _transformSystem = _entityManager.System<SharedTransformSystem>();
    }

    public void RenderSprites(DrawingHandleScreen handle, MapCoordinates mapPos, Angle rotation, float fov, Vector2 screenSize, float[] zBuffer)
    {
        var playerPos = mapPos.Position;
        var dir = rotation.ToVec();

        float planeLength = (float)Math.Tan(MathHelper.DegreesToRadians(fov) / 2.0);
        var plane = new Vector2(-dir.Y, dir.X) * planeLength;

        // Inverse camera matrix determinant
        float invDet = 1.0f / (plane.X * dir.Y - dir.X * plane.Y);

        int screenWidth = (int)screenSize.X;
        int screenHeight = (int)screenSize.Y;

        var spritesToDraw = new List<SpriteDrawCommand>();

        var tagSystem = _entityManager.System<TagSystem>();

        var query = _entityManager.EntityQueryEnumerator<SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spriteComp, out var xform))
        {
            if (!_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
                continue;

            // Пропускаем невидимые спрайты
            if (!sprite.Visible)
                continue;

            // Пропускаем стены, так как они рендерятся DDA алгоритмом
            if (tagSystem.HasTag(uid, "Wall"))
                continue;

            // Пропускаем плоские объекты под полом, провода и маркеры
            if (sprite.DrawDepth <= (int)Content.Shared.DrawDepth.DrawDepth.FloorObjects)
                continue;

            if (uid == _playerManager.LocalEntity)
                continue; // Не рисуем сами себя

            if (xform.MapID != mapPos.MapId)
                continue;

            // Относительная позиция спрайта к игроку
            var diffX = xform.MapPosition.X - playerPos.X;
            var diffY = xform.MapPosition.Y - playerPos.Y;

            // Расстояние
            float dist = (float)Math.Sqrt(diffX * diffX + diffY * diffY);
            if (dist > 30f || dist < 0.2f)
                continue;

            // Трансформация в Camera Space
            float transformX = invDet * (dir.Y * diffX - dir.X * diffY);
            float transformY = invDet * (-plane.Y * diffX + plane.X * diffY);

            // Если transformY <= 0, значит спрайт позади игрока
            if (transformY > 0)
            {
                var texture = GetFlattenedTexture(uid, spriteComp);
                if (texture != null)
                {
                    spritesToDraw.Add(new SpriteDrawCommand
                    {
                        Uid = uid,
                        Texture = texture,
                        TransformX = transformX,
                        TransformY = transformY
                    });
                }
            }
        }

        // Z-Сортировка (Painter's Algorithm) — рисуем от дальних к ближним
        spritesToDraw.Sort((a, b) => b.TransformY.CompareTo(a.TransformY));

        int stepPixel = 2; // Шаг оптимизации (resolutionStep)

        // Расстояние до проекционной плоскости — та же формула, что и для стен
        float distToProj = screenWidth / (2.0f * planeLength);

        foreach (var cmd in spritesToDraw)
        {
            int spriteScreenX = (int)((screenWidth / 2) * (1 + cmd.TransformX / cmd.TransformY));

            // Оригинальное соотношение сторон текстуры (ширина / высота)
            float aspect = (float)cmd.Texture.Width / cmd.Texture.Height;

            // Высота спрайта на экране = distToProj / distance (согласовано со стенами)
            int spriteHeight = Math.Abs((int)(distToProj / cmd.TransformY));

            // Ширина пропорционально высоте и оригинальному аспекту
            int spriteWidth = (int)(spriteHeight * aspect);

            int drawStartY = -spriteHeight / 2 + screenHeight / 2;
            if (drawStartY < 0) drawStartY = 0;
            int drawEndY = spriteHeight / 2 + screenHeight / 2;
            if (drawEndY >= screenHeight) drawEndY = screenHeight - 1;

            int drawStartX = -spriteWidth / 2 + spriteScreenX;
            int drawEndX = spriteWidth / 2 + spriteScreenX;

            int clampedStartX = drawStartX < 0 ? 0 : drawStartX;
            int clampedEndX = drawEndX >= screenWidth ? screenWidth - 1 : drawEndX;

            for (int stripe = clampedStartX; stripe < clampedEndX; stripe += stepPixel)
            {
                // Проверка Occlusion c помощью _zBuffer
                if (cmd.TransformY < zBuffer[stripe])
                {
                    float texXRatio = (stripe - drawStartX) / (float)spriteWidth;
                    int texX = (int)(texXRatio * cmd.Texture.Width);

                    var rect = new UIBox2(stripe, drawStartY, stripe + stepPixel, drawEndY);
                    var subRegion = new UIBox2(texX, 0, texX + 1, cmd.Texture.Height);

                    handle.DrawTextureRectRegion(cmd.Texture, rect, subRegion);
                }
            }
        }
    }

    private Texture? GetFlattenedTexture(EntityUid uid, SpriteComponent sprite)
    {
        // Пока мы берем просто Default Icon. Позже можно реализовать полноценный IClyde.RenderToTarget для слоистых персонажей.
        return sprite.Icon?.Default;
    }
}
