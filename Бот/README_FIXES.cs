/*
Скрипты в обьекте Бота
Бот - Suspect из игры Ready Or Not

SmartEnemyAI (Core Hub)
├── AIPerceptionModule (зрение, слух, память)
├── AIUtilityBrain (чистый utility decision making)
├── AIActionExecutor (выполнение действий)
├── AIAnimationModule (анимации, look at)
├── AICombatModule (стрельба, прицеливание)
├── AIMovementModule (навигация, укрытия)
└── AISquadModule (командная работа)

Подробное описание модулей:
- AIPerceptionModule - отвечает за восприятие мира ботом, зрение, слух и память
- AIUtilityBrain - отвечает за принятие решений ботом на основе utility системы
- AIActionExecutor - отвечает за выполнение действий, которые бот выбирает BestAction и BestCombatMoveAction из AIUtilityBrain
- AICombatModule - отвечает за все что связано со стрельбой, прицеливанием и выбором оружия
- AIMovementModule - отвечает за навигацию бота по миру, выбор укрытий,перемещение, взаймодействие с дверями и миром
- AISquadModule - отвечает за командную работу ботов, прикрытие друг друга, помощь, фланг, и загон игрока в засаду
- AIAnimationModule - отвечает за проигрывание анимаций бота где:
SPEED = ходьба/бег
WEAPON_RAISED = Оружие наготове, готов стрелять
DECIDING = На мушке игрока и думает стрелять или поднять руки
SUPPRESSED = Полу сдался, руки подняты ,стоя, но оружие еще в руках, может обмануть и выстрелить или бросить оружие и окончательно сдатся
SURRENDERED =   Окончательно сдался,на колени, готов к арресту
IN_COVER = в укрытии
DEAD = 
BEING_CUFFED = Процесс ареста, может прерыватся
ARRESTED = Аррестован
HIT = получение урона
все остальное это вторичные скрипты, которые взаимодействуют с основными модулями

========== ИСПРАВЛЕНИЯ 2024 ==========

ИСПРАВЛЕНО (сессия 1):
✅ AIPerceptionModule.OnHearSound() - добавлена реакция на SoundType.VoiceShout
   - Бот теперь реагирует на голосовые команды игрока
   - HandleVoiceCommand() - разворачивается на голос даже со спины
   - Устанавливает IsDeciding=true для анимации раздумия

✅ AIPerceptionModule.ClearTarget() - улучшена память бота
   - Бот НЕ ЗАБЫВАЕТ позицию игрока мгновенно
   - Сохраняет LastKnownThreatPosition при потере визуального контакта
   - ThreatLevel понижается до Suspected но бот помнит где видел угрозу

✅ PlayerPressureSystem.ExecuteVoiceCommand() - убрано ограничение угла
   - Удалена проверка enemyAngle > 120° которая блокировала реакцию со спины
   - Теперь голос слышен всегда если игрок смотрит на врага

✅ AIAnimationModule - добавлен Debug режим
   - showDebugLogs для диагностики состояний анимации
   - Логирует когда DECIDING/SUPPRESSED/SURRENDERED = true

ИСПРАВЛЕНО (сессия 2):
✅ AIPerceptionModule.HandleVoiceCommand() - ПОЛНАЯ переработка
   - Если близко (<8м) → вызывает SurrenderDecision.StartDecision() 
   - Бот реально входит в IsDeciding состояние с анимацией
   - Добавляет стресс (+15) при голосовой команде

✅ AIPerceptionModule.HandleNearbyGunshot() - НОВОЕ
   - Реакция на предупреждающий выстрел (<10м)
   - Стресс зависит от дистанции (до +30)
   - Если уже IsDeciding - ускоряет решение (сдаться или драться)
   - Если <5м - сразу начинает раздумье

✅ AIMovementModule.UpdateDoorInteraction() - УЛУЧШЕНО
   - Боты не подходят вплотную к дверям
   - Проверяют что дверь НА ПУТИ движения (dotProduct > 0.5)
   - Держат безопасную дистанцию
   - Разблокировка движения после открытия двери

✅ ReactiveCombatLayer.Reflex_EngageVisibleThreat() - УЛУЧШЕНО
   - Бот держит фокус на последней позиции до 10 сек (было 3)
   - 0-3 сек: подавляющий огонь
   - 3-10 сек: ждет и целится, не отворачивается
   - Не дергается и не теряет направление

✅ AIActionExecutor.AssignSquadRoles() - РАНДОМИЗАЦИЯ
   - Роли распределяются рандомно (Fisher-Yates shuffle)
   - Каждый раз разные боты получают роли Leader/Suppressor/Flanker

ИСПРАВЛЕНО (сессия 3 - Анимации):
✅ AIAnimationModule - добавлены методы явного вызова анимаций:
   - TriggerSurrender() - анимация сдачи
   - TriggerDeciding() / StopDeciding() - анимация раздумия
   - TriggerBeingArrested() - начало ареста
   - TriggerArrested() - завершение ареста
   - TriggerSuppressed() / StopSuppressed() - анимация подавления

✅ PsychologyModule - вызывает анимации:
   - TriggerSurrender() при ForceSurrender() и TriggerSurrender()
   - TriggerSuppressed() при OnSuppressed() если стресс > 60%
   - CheckSuppressionState() - автоматическое снятие подавления по таймеру

✅ AISurrenderDecisionModule - вызывает анимации:
   - TriggerDeciding() при StartDecision()
   - StopDeciding() при MakeDecision() и InterruptDecision()

✅ AIArrestModule - вызывает анимации:
   - TriggerBeingArrested() при StartArrest()
   - TriggerArrested() при CompleteArrest()

✅ AIMovementModule - двери как укрытие:
   - FindNearbyDoorForCover() - поиск двери для укрытия
   - UseDoorAsCover() - использование двери как укрытие
   - OnAIClose() - бот закрывает дверь за собой

ТЕКУЩАЯ ОТЛАДКА:
ЧтоРаботает:
- Визуальная система бота работает прекрасно
- Психология бота работает НО можно и рандомнее
- Визуальная реакция и реактивный модуль в целом 
- Реализм стрельбы (AICombatModule)
- НОВОЕ: Реакция на голос игрока (включая со спины)
- НОВОЕ: Память бота улучшена (держит фокус до 10 сек)
- НОВОЕ: Реакция на предупреждающий выстрел
- НОВОЕ: Роли распределяются рандомно

Что осталось проверить:
- AISquadModule - Командная работа ботов (координация)
- AIUtilityBrain - иногда странные решения
- AIAnimationModule - Проверить что DECIDING анимация играется

нужны другие модули? ищи в папке Assets\CUSTOM\Script\Gameplay\AI3
*/
