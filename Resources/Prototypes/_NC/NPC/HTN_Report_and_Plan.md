### Отчет о ванильных системах HTN и AI в Space Station 14

#### Обзор HTN (Hierarchical Task Network)
HTN в SS14 — это система планирования поведения NPC, основанная на иерархических задачах. Она позволяет NPC выполнять сложные последовательности действий через декомпозицию задач на подзадачи. Система использует:
- **Compound Tasks** (составные задачи): Разбиваются на подзадачи с условиями (preconditions) и приоритетами (branches). Примеры: планирование боя, отдыха.
- **Primitive Tasks** (примитивные задачи): Базовые действия, выполняемые через operators (операторы). Примеры: стрельба, движение, ожидание.
- **Preconditions**: Условия для выполнения задач (наличие цели, расстояние, LOS — линия видимости).
- **Services**: Фоновые процессы, например, выбор целей (UtilityService).
- **Blackboard**: Хранение состояния (цели, ключи).

NPC получают HTN-компонент, где устанавливается rootTask (корневая задача). Планировщик (HTNSystem) декомпозирует задачи и выполняет operators.

#### Ключевые существующие реализации
На основе анализа кода и прототипов (Resources/Prototypes/NPCs/ и _NC/NPC/HTN/):

1. **SimpleHumanoidHostileCompound** (из root.yml):
   - Branches: RangedCombatCompound → MeleeCombatCompound → IdleCompound.
   - Описание: Стрельба на расстоянии, ближний бой, отдых. Использует GunOperator для стрельбы, MeleeOperator для атаки, MoveToOperator для подхода.

2. **IdleCompound** (из idle.yml):
   - Branches: Выбор места (PickAccessibleOperator), движение (MoveToOperator), ожидание (WaitOperator).
   - Описание: NPC выбирает случайное место в радиусе и ждет там. Может вращаться (IdleSpinCompound).

3. **RackGunCompound** (из bandit_htn.yml и NC-специфично):
   - Branches: Использование предмета в руке (NPCUseInHandOperator) для перезарядки/очистки оружия.
   - Описание: Проверяет precondition (NCWeaponMaintenancePrecondition) на jammed/not racked оружие.

4. **NCBanditRootCompound** (из bandit_htn.yml):
   - Branches: RackGunCompound (приоритет 1, если оружие jammed), SimpleHumanoidHostileCompound (приоритет 2), IdleCompound (приоритет 3).
   - Описание: Специфично для NC-бандитов: обслуживание оружия, бой, отдых.

5. **Другие compound tasks**:
   - TurretCompound/EnergyTurretCompound: Для турелей — стрельба в LOS, вращение.
   - SimpleRangedHostileCompound: Ranged → Melee → Idle.

6. **Primitive Operators** (из Content.Server/NPC/HTN/PrimitiveTasks/Operators/):
   - GunOperator: Стрельба по цели в LOS/range.
   - MeleeOperator: Ближний бой.
   - MoveToOperator: Движение к цели с pathfinding.
   - WaitOperator: Ожидание.
   - PickAccessibleOperator: Выбор доступного места.
   - NPCUseInHandOperator: Использование предмета в руке (для перезарядки).

7. **Preconditions/Services**:
   - KeyExistsPrecondition, TargetInRangePrecondition, TargetInLOSPrecondition.
   - UtilityService для выбора целей (NearbyGunTargets).

#### Ограничения и отсутствующие механики
- Нет задач для **укрытий** (cover-seeking): NPC не ищут укрытия при бое.
- Нет **патрулирования** в смысле охраны территории (только случайное движение в Idle).
- Нет **флангов** или координации между NPC.
- Нет бросания гранат или использования специальных предметов (кроме in-hand).
- Нет сложной тактики (например, отвлечение или групповая атака).
- Все поведение реактивное: реагирует на цели, но не планирует долгосрочные стратегии.

Система расширяема: можно добавлять новые compound/primitive tasks, но требует кода (operators) для новых механик.

### План по улучшению NPC (Бандиты, Militech, Biotechnica) с использованием HTN

Фокус на существующих механиках HTN. Уровни сложности: Слабые (Бандиты), Средние (Biotechnica), Сильные (Militech). Улучшения через модификацию rootTask, добавление branches в compound tasks или новых compound на основе существующих. Если механика отсутствует, отмечу как "*механика* (этого нет)".

#### Общий подход
- Использовать NCBanditRootCompound как базу для всех.
- Увеличить сложность: больше branches, preconditions, services.
- Для уровней: Слабые — базовое, Средние — добавление тактики, Сильные — агрессивность и координация.
- Тестировать через NPCSystem и HTNSystem.

#### 1. Слабые NPC: Бандиты (Boosters)
   - **Текущий rootTask**: NCBanditRootCompound (RackGun → SimpleHumanoidHostile → Idle).
   - **Улучшения**:
     - Расширить IdleCompound: Увеличить range в PickAccessibleOperator для "патрулирования" (случайное движение по большей области, имитируя охрану). Добавить precondition на отсутствие целей для более частого патрулирования.
     -!!Вот это не надо!! ( Улучшить RackGunCompound: Добавить branch для проверки ammo (если есть precondition на low ammo, использовать NPCUseInHandOperator для перезарядки из инвентаря, но это требует нового operator — *NPCReloadOperator (этого нет)*). )
     - Добавить branch в SimpleHumanoidHostileCompound: После стрельбы — движение назад (новый compound на основе Idle, но с precondition на recent combat).
     - Результат: Более мобильные бандиты, лучше обслуживают оружие, но остаются простыми.

#### 2. Средние NPC: Biotechnica
   - **Текущий rootTask**: NCBanditRootCompound (аналогично бандитам).
   - **Улучшения**:
     - Модифицировать SimpleHumanoidHostileCompound: Добавить precondition в RangedCombatCompound для поиска укрытий перед стрельбой (*CoverSeekingPrecondition и MoveToCoverOperator (этого нет)*). Если нет — использовать существующий MoveToOperator с random offset для "укрытия".
     - Добавить новый compound: "SupportCompound" (на основе Idle, но с UtilityService для выбора союзников). Branch: Если союзник в бою, двигаться к нему (MoveToOperator) и атаковать цель союзника (GunOperator с targetKey от союзника). *Координация между NPC (этого нет)*.
     - Улучшить MeleeCombatCompound: Добавить WaitOperator после атаки для "передышки", имитируя осторожность.
     - Результат: Более тактичные, поддерживают друг друга, используют позиционирование, но без новых operators.

#### 3. Сильные NPC: Militech
   - **Текущий rootTask**: NCBanditRootCompound.
   - **Улучшения**:
     - Сделать SimpleHumanoidHostileCompound более агрессивным: Убрать Idle из branches, приоритет на RangedCombat (стрельба до конца ammo). 
     - !!Вот это не надо пока что!!(Добавить branch для бросания гранат (*GrenadeThrowOperator (этого нет)*) с precondition на группу врагов.)
     - Добавить "FlankCompound": Новый compound с branches: Выбрать цель (UtilityService), двигаться сбоку (MoveToOperator с offset), атаковать (GunOperator). *Фланговая тактика (этого нет)*.
     - Улучшить RackGunCompound: Добавить branch для использования медикаментов (NPCUseInHandOperator с precondition на damage).
     - Добавить precondition в rootTask: Если HP низкое, приоритет на retreat (новый compound на основе Idle с движением от цели).
     - Результат: Агрессивные, тактичные NPC с выживанием, но требуют новых operators для гранат/фланга.

#### Реализация и тестирование
- Создать новые compound tasks в _NC/NPC/HTN/ (например, MilitechRootCompound.yml).
- Обновить rootTask в NPC-прототипах.
- Тестировать: Запустить NPC, проверить планирование через HTNSystem (логи в HTNSystem.cs).
- Если нужны новые operators: Создать в Content.Server/_NC/NPC/HTN/PrimitiveTasks/Operators/.
- Валидация: Убедиться, что NPC не застревают (HTNSystem имеет fail-safe).

Этот план использует существующие механики максимально, добавляя сложность через комбинации. Для отсутствующих механик (укрытия, координация, гранаты) потребуется разработка новых operators. Готов обсудить детали или начать реализацию.