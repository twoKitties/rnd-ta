# NETCODE.md

Карта сетевых решений проекта: **какая механика, какими классами сделана, через что синхронизируется.**
Составлено по коду 2026-08-12, обновлено под поток `Loading → Menu → Hub → Level → Hub` того же дня. `CLAUDE.md` остаётся источником правды по причинам и историям поломок — здесь только карта; числа и тюнинг живут в `MECHANICS.md` и на префабах.

## Стек

FishNet, транспорт Tugboat (LiteNetLib). Ставится через `Packages/manifest.json` (git-URL без пина — что реально разрешилось, смотреть в `Library/PackageCache/`). Реестр сетевых префабов — `Assets/DefaultPrefabObjects.asset`, генерируется FishNet, **руками не править**.

Сетевые префабы: `Assets/_Game/Content/Shared/` — `Session.prefab` (`NetworkManager` + `Tugboat` + `RaidSession`), `LobbyRoster.prefab`, `RaidState.prefab`. Спавнятся, в сценах не лежат. `LobbyRoster.prefab` — **единственный `IsGlobal` объект в проекте**: сессия живёт в хабе, а не в меню, и ростер обязан пережить выгрузку меню. FishNet уводит такой объект в `DontDestroyOnLoad` (`NetworkObject.TryStartDeactivation`), поэтому `ReplaceOption.All` его не трогает.

Топология: **server-authoritative, хост — один из игроков.** Миграции хоста нет (`RaidSession.Leave`). Позднее подключение открыто, пока все стоят в хабе, и закрыто с момента вылета в локацию (`RaidSession.IsRaidRunning` в `OnRemoteConnectionState`).

## Три инварианта

1. **Сценовых `NetworkObject` в проекте нет и быть не должно.** Реплицируемое состояние живёт на **спавнящемся** префабе, сценовый объект держит на него обычную ссылку: `LevelGoal` → `RaidState`, `Door` → `DoorState`, `MenuView` → `LobbyRoster`. Почему — в `CLAUDE.md`.
2. **Спросить / решить / применить — три разные вещи.** Клиент шлёт `ServerRpc`, сервер перепроверяет правило своими данными, состояние уезжает `SyncVar`/`SyncList`, и `OnChange` применяют **все пиры, включая авторитет** — чтобы к картинке вёл ровно один путь. Образцы: `Pet.CanBeTakenBy`/`TryTake`/`ApplyCarry`, `DoorState.Use`/`ServerUse`, `LobbyRoster.RequestReady`.
3. **Уровень без сети — поддерживаемый режим.** Play прямо в `Level.unity`: `RaidState.Current`/`DoorState.Current` равны null, и это законный ответ «мы одни», а не сбой. «Я в сети?» спрашивается у закешированного `NetworkObject` (`_nob.IsSpawned`), а не у `NetworkBehaviour.IsSpawned` — последнее бросает исключение на неинициализированном компоненте.

## Каталог механик

### Сессия, меню и хаб

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Соединение, host/join/leave, разрывы | `Code/App/RaidSession.cs` (обычный `MonoBehaviour`), `Content/Shared/Session.prefab` | **Нет канала.** Колбэки транспорта + три бэкстопа: `Transport.SetTimeout(deadPeerTimeout)`, ежекадровая проверка «подключены ли мы ещё к чему-нибудь», `orphanTimeout` по пропаже своего аватара |
| Причина, по которой сессия кончилась | `RaidSession.ConsumeNotice` → `MenuView.Awake` | Хранимое значение, не событие: сессия кончается в `Level` или в хабе, а сказать об этом может только попап меню — сценой позже |
| Кто здесь и потолок в 4 игрока | `Code/App/LobbyRoster.cs` (+ `PlayerLobbyUI.cs`, пока никем не показывается) | **`SyncList<LobbySlot>`** + **`ServerRpc(RequireOwnership = false)`** `RequestReady` / `RequestName`. Слот выбирается по `NetworkConnection sender`, не по аргументу — чужую строку изменить нельзя. С переездом сборной в хаб UI списка временно отсутствует, а сам ростер продолжает отбивать пятого |
| Смена сцен (Menu → Hub → Level → Hub) | `RaidSession.StartRaid` / `GoToHub` → `LoadForEveryone`, `Code/UI/LoadingScreen.cs` | **FishNet `SceneManager.LoadGlobalScenes`**, `ReplaceOption.All`. Что ждать загрузочному экрану, едет **одним байтом в `SceneLoadData.Params.ClientParams`** (`ServerParams` рядом — `[NonSerialized]`, не путешествует). Присоединившийся получает уже загруженные глобальные сцены вместе с параметрами последней загрузки (`SceneManager.OnClientAuthenticated`), поэтому клиент из меню попадает в хаб сам и `MenuView` ничего не грузит |
| Аватары в хабе | `Code/Hub/HubBootstrapper.cs` | **`ServerManager.Spawn(avatar, connection)` по присутствию соединения в сцене** (`SceneManager.OnClientPresenceChangeEnd`). Аватар принадлежит одному соединению, поэтому «ждать всех», как в `LevelBootstrapper`, здесь не нужно — и тем же кодом покрывается тот, кто подключился позже |
| Выбор локации и вылет | `Code/Hub/HubMenuUI.cs` → `RaidSession.StartRaid(sceneName)` | **Канала нет:** выбор — решение хоста, а клиенту приезжает уже загрузка сцены. Список локаций у клиента не строится |

### Уровень и владение

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Расстановка акторов | `Code/Level/LevelBootstrapper.cs`, `Code/Spawning/ActorSpawner.cs` | **`ServerManager.Spawn`**: аватар — с `connection` (владелец), животные / Old Man / `RaidState` — без владельца. Клиент не создаёт ничего. Без сети — `nob.SetIsNetworked(false)`, иначе FishNet выключит объект |
| «Этот аватар — мой» | `Code/Player/LocalAvatar.cs` | **Владение `NetworkObject`** (`IsOwner` в `OnStartClient`). По проводу ничего; включается список локальных компонентов — камера, `AudioListener`, HUD, ввод |
| «Этого актора считает только сервер» | `Code/App/ServerSimulated.cs` (Dog / Kitty / Parrot / Old Man) | **Канала нет:** на не-серверных пирах гасится список `Behaviour` — мозг и `NavMeshAgent`. Гейт — список ссылок на префабе, а не ветка внутри AI-кода |
| Счётчики рейда, исход и длительность | `Code/Level/RaidState.cs` (спавнится сервером); правила — `Code/Level/LevelGoal.cs`, обычный `MonoBehaviour` | **Пять `SyncVar`**: `_delivered`, `_total`, `_isWon`, `_isLost`, `_duration`. Длительность пишется сервером один раз, в `SetOutcome`, и отсчитывается от `OnStartServer` — время жизни этого объекта и есть рейд. `LevelGoal` читает `RaidState.Current`, а без сети — собственные поля |
| Статистика на итоговой панели | `Code/UI/EndScreenUI.Summarise` | **Своего канала нет:** животные — из `RaidState`, время — оттуда же, выжившие считаются локально по `LevelBootstrapper.SensedPlayers` (`PlayerLife._dead` уже реплицирован) и **снимаются один раз**, потому что Old Man продолжает стрелять под открытой панелью |

### Игрок

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Положение и поворот | `Code/Player/PlayerController.cs` + `NetworkTransform` на `Player.prefab` | **`NetworkTransform`, `_clientAuthoritative: 1`** — владелец двигает себя сам, остальные интерполируют |
| Описание движения: состояние, скорость, присед | `Code/Player/PlayerMotion.cs`; читают `PlayerNoise`, `PlayerAnimator`, `AI/SensedPlayer`, `Pet.CanBeTakenBy` | **Три `SyncVar`** (`MoveState`, `float`, `bool`) + **`ServerRpc Report`** с обязательным владением: только владелец знает, какие клавиши зажаты. Шлётся по изменению, скорость — с порогом `speedEpsilon`. **Не** в списке `LocalAvatar` — обязан работать на всех пирах |
| Наклон камеры (питч) | `PlayerMotion` → `PlayerController.ApplyPitch`; читает `SpectatorCamera` | **`SyncVar<float>` + `ServerRpc ReportPitch`**, с 2026-08-12. Единственное значение в проекте, идущее **темпом** (`pitchInterval`), а не по изменению: голова меняет угол каждый кадр. На чужих пирах догоняется за один интервал, иначе 10 Гц читаются ступеньками. Нужен только зрителю — правила смотрят на центр капсулы |
| Смерть | `Code/Player/PlayerLife.cs` | **`SyncVar<bool> _dead`**. Решает только `DecidesHere` (сервер или процесс без сети), все пиры выполняют `ApplyDeath` из `OnChange`. Спрашивается у **процесса** (`InstanceFinder`), а не у spawn-состояния объекта: выходящего игрока убивают по дороге наружу, когда объект уже деспавнится |
| Наблюдение за живым товарищем | `Code/Player/SpectatorCamera.cs`, `FirstPersonBody.cs` | **Своего канала нет:** копирует мировой трансформ чужого `CameraRoot`, живых берёт из `LevelBootstrapper.SensedPlayers` |
| Шаги | `Code/Audio/FootstepAudio.cs` | **Своего канала нет и не должно быть.** Темп — из движения `NetworkTransform`, громкость — из `PlayerController.State`, который уже реплицирован `PlayerMotion` |

### Животные

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Положение | `Code/Pets/PetBrain.cs` + `NetworkTransform` | **`NetworkTransform`, `_clientAuthoritative: 0`** — двигает только сервер |
| Анимация | `PetBrain` + `NetworkAnimator` | **`NetworkAnimator`, `_clientAuthoritative: 0`**: параметры ставит серверный мозг, они уезжают клиентам |
| Переноска (кто кого несёт) | `Code/Pets/Pet.cs`, `Code/Player/PlayerHands.cs` | **`SyncVar<NetworkObject> _carrierObject`** + **`ServerRpc`** `RequestTake` / `RequestRelease`. Сервер перепроверяет `CanBeTakenBy` и своего носителя, а не заявленного клиентом. Все применяют `ApplyCarry`/`ApplyRelease` из `OnChange`; поза в руках считается локально в `LateUpdate` |
| Животное на борту тарелки | `Pet.Deliver` | **Отдельный `SyncVar<bool> _delivered`**: «отпустили» и «сдали» — разные состояния, и только одно убирает животное с уровня |
| Лай при обнаружении | `Pet.AnnounceNoticed`, `Code/Pets/PetVoice.cs` | **`ObserversRpc(RunLocally = true)`**: мозг лает на сервере, а предупреждение нужно остальным троим |

### Двери

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Открыть / закрыть створку | `Code/Doors/Door.cs` — **обычный `MonoBehaviour`**; реплицируемая половина — `Code/Doors/DoorState.cs` на `RaidState.prefab` | **`SyncList<float> _swings`** (целевой угол на створку) + **`ServerRpc RequestUse(index)`**. Створки нумеруются путём из sibling-index'ов, чтобы все машины считали их одинаково; сервер перепроверяет дистанцию (`Door.ServerReach`) и сторону — по аватару, которым владеет это соединение. Без сети створка решает сама; в сети, но пока `DoorState` не заспавнился, нажатие **отбрасывается**, а не применяется локально |

### Old Man

| Механика | Классы | Синхронизация |
| --- | --- | --- |
| Положение и анимация | `Code/OldMan/OldManBrain.cs` + `NetworkTransform` и `NetworkAnimator`, оба `_clientAuthoritative: 0` | То же, что у животных |
| Носит / вскидывает винтовку, куда целится | `Code/OldMan/RifleRig.cs` | **Два `SyncVar`**: `bool _aiming`, `Vector3 _aimPoint`. Вся поза, блендинг carry↔aim и IK считаются локально из этой пары — поза по проводу не едет |
| Выстрел: вспышка, звук, отдача | `Code/OldMan/ShotFlash.cs` | **`ObserversRpc(RunLocally = true)` `ObserversFire(Vector3 aimPoint)`** — одна RPC несёт всё сразу: вспышку, звук, `RifleRig.Kick()` и точку прицела |
| Дробина и убийство | `Code/OldMan/ShotPellet.cs` | **Не сетевой объект вообще.** Каждый пир выращивает свою из той же RPC: муззл — трансформ `ShotFlash.flash`, направление — точка прицела **из аргумента RPC** (с 2026-08-12; до этого бралась из SyncVar `RifleRig`, шедшей своим темпом). Боевая только там, где `DecidesHere`; смерть приезжает через `PlayerLife._dead` |

### Сдача животного в луч

Правило (`BeamZone.Contains` по позиции носителя) живёт в `Code/Level/LevelGoal.cs` — обычном `MonoBehaviour`, который не может нести RPC. Поэтому клиент шлёт **`ServerRpc RaidState.RequestRelease(carrier)`** и локально **не делает ничего**; сервер проверяет, что аватар принадлежит этому соединению, перепрогоняет `CountsAsDelivery` своим `BeamZone`, отпускает животное и увеличивает счётчик. `PlayerLife` при смерти носителя идёт тем же путём, поэтому убитый **внутри** луча всё равно сдаёт животное.

## Что сознательно не синхронизируется

- **Шум, слух, зрение, двери-для-AI:** `Code/Noise/NoiseEmitter.cs`, `Code/AI/Hearing.cs`, `Sight.cs`, `DoorGate.cs`, `SensedPlayer.cs`. Живут только там, где считается AI (`ServerSimulated`). Исключение — `PlayerNoise`: он работает на всех пирах, но читает уже реплицированный `PlayerMotion`.
- **Весь звук.** Правило: звук вешается на изменение состояния, которое **уже** применено на каждом пире (`Pet.ApplyCarry`/`AnnounceNoticed`, `Door.ApplySwing`, `ShotFlash.ObserversFire`, `PlayerController.State` через `PlayerMotion`, трансформ через `NetworkTransform`). Если звуку понадобился новый `SyncVar` — его вешают не туда.
- **Сброс состояния между рейдами.** Сыграть ещё раз — это возврат в хаб и новый вылет, то есть настоящая перезагрузка сцены; всё залатченное пересоздаётся `LevelBootstrapper`, кода сброса нет нигде. Прежний трюк с промежуточным лобби (FishNet не грузит уже загруженную сцену) исчез вместе с `RaidSession.Restart`.

## Известные пробелы

Открыты на 2026-08-12, подробности и приоритет — в очереди фиксов (см. отчёты `netcode-readiness-auditor`).

- **Сервер судит дистанционные правила по устаревшей позиции клиента** — `Pet.captureDistance`, `BeamZone.radius` против интерполяции и RTT/2; `Pet.TryTake` возвращает true на отправке, `RaidState.RequestRelease` не отвечает ничего. У хоста работает, у клиента — через раз. Лечится тем же запасом, что уже есть у двери (`Door.serverReach` против `reach`), но значение нужно взять из замера RTT на двух машинах — **единственный пункт, который ждёт замера, а не работы**.
- **Числа спавна и лобби не связаны**: `PlayerSpawn` в `Level` ровно 4 против `LobbyRoster.maxPlayers` 4, запаса нет, и ничто не следит за тем, чтобы они совпадали. Пятый игрок физически некуда встать; сегодня его не пускает ростер, но числа держатся в согласии вручную. В хабе точек тоже 4, но там они раздаются по кругу (`_served.Count % _points.Count`), так что лишний игрок встал бы в чужую.
- **Хаб проверен в Play только в одном процессе** (хост, он же единственный игрок): `Menu → Hub → Level → Hub → Level` проходит целиком, аватары спавнятся, итоговая панель со статистикой появляется, повторный вылет собирает уровень с нуля. **`IsGlobal`-ростер измерен и работает** — пережил все четыре `ReplaceOption.All` со своим слотом. **Не измерено ничего клиентского:** что присоединившийся получает хаб автоматически, позднее подключение, и вообще вторая машина.

Закрыто 2026-08-12: питч реплицирован, точка прицела переехала в RPC выстрела, у «аватар не приехал вообще» появился бэкстоп (`RaidSession._hasAvatar` + арм по сцене уровня), `DoorGate` возвращает ближайшую створку.
