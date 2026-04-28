## By PvrG
# WILDCARD WHITE DREAM PROJECT INDIVIDUAL CONTRIBUTOR LICENSE AGREEMENT V.1

Это файл-гайд для музыкальных зон.

У нас есть рабочая зона:

1. Resources\Prototypes\Entities\Markers\Music\music.yml
# Это прототип МАРКЕРА зоны, в спавн меню
---------------------------------------
2. Resources\Prototypes\Audio\music.yml
# Это прототип с несколькими важными компонентами:
   - ingameMusic
   - soundCollection
   - rules
---------------------------------------
3. Resources\Audio\_White\Music
# Сама папка с музыкой, можно указать любой путь в шаге 2 (soundCollection)
---------------------------------------

Разберём написание прототипа:

1. Открываем Resources\Prototypes\Audio\music.yml)
2. Нужно создать в soundCollection с вашей музыкой из Resources\Audio\_White\Music (или любого другого пути, который вы укажите там)
3. Нужно создать в ingameMusic с вашей soundCollection в шаге 2
4. Нужно создать в rules (но я не сильно разобралась, но у меня там стоит - !type:InArea (если что, если разберётесь в правилах, прошу мне отписать в ДС @PvrG))
5. Открыть и создать маркер !ВАЖНО! чтобы id маркера был одинаковы как в шаге 4 в зоне например:

# Маркер
- type: entity
  id: !!!AreaMusicChurch!!!
  parent: AreaMarkerBase
---------------------------------------
# Правило
- type: rules
  id: Church
  rules:
    - !type:InArea
      id: !!!AreaMusicChurch!!!
