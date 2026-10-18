# NekoRay NT5 — ход работ (этап «1.0»)

Файл для продолжения работы без повторного анализа. Пути вида `NekoRay-NT5-build-lab\...` ниже — локальная
рабочая папка разработчика (тулчейн, тестовые комплекты в `versions`; комплекты до 0.9.6-rc2 — в архиве
`baseline\vm-xp-test-history-*.7z`); в репозиторий она не входит и для сборки не нужна (сборка — `BUILDING.md`).

## 2026-10-18: 0.9.7-dev1 — одна сборка для Windows 2000 SP4 и XP SP3; очистка репозитория

- Рантайм Go 1.20 (бэкпорт для XP) перенесён на NT 5.0: `nt5-ui/third_party/go1.20-nt51-nt50.patch`
  (обработка исключений без VEH, CryptGenRandom, connect без ConnectEx, gethostbyname без
  GetAddrInfoW, PE 5.0). Те же `xray.exe` и `nt5helper.exe` для обеих систем.
- Проверки на хосте: self-test 351/351, UI smoke 52/52 ×4, тесты nt5helper, e2e VLESS+REALITY+Vision
  (`nt5-ui/tests/e2e-reality.sh`), зонд рантайма (`nt5-ui/tests/nt50probe`, в т.ч. проверка
  `tcp-refused` по коду 10061, а не по тексту ошибки).
- По сообщению пользователя на Windows 2000 SP4 (с .NET Framework 2.0 и установленными
  сертификатами, **без KernelX**) и на Windows XP SP3 (без расширений системы) работают ссылки VLESS,
  HTTP-подписки, URL Test, подключение, отключение, переподключение и трафик браузера через прокси.
  Точная конфигурация и ограничения — `docs/nt5-win2000.md`.
- Репозиторий очищен от исходников исходного Nekoray (C++/Qt), которые сборка NT5 не использует;
  добавлены `BUILDING.md` и `.gitignore` проекта.
- Опубликовано: репозиторий https://github.com/quargelk/NekoRay-NT5 с корневым `README.md`
  (английский) и `README.ru.md` (русский), снимками экрана в `docs/screenshots/` и пререлизом
  разработки `v0.9.7-dev1` (заметки к выпуску — `docs/releases/v0.9.7-dev1.md`).

## 2026-10-17 (позже): результаты XP по `-portfix-test`; план первого опыта на Windows 2000

### Результаты ручной проверки на XP SP3 (сообщены пользователем)

Комплект `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.6-rc2-portfix-test`.
Таблица по строкам — в `docs/nt5-test-checklist.md`, раздел «Результаты на
Windows XP SP3».

- **PASS по построчному отчёту:** M1–M4 (исправление порта 2081 подтверждено
  на XP, повторные переключения, Exit/запуск, настоящий занятый порт);
  языковые проверки L2–L6 (в отчёте названы «F1–F4»).
- **PASS по более ранним отчётам (`-rc2`):** запуск, Diagnostics,
  VLESS + REALITY + Vision, импорт подписки, Happ-HWID, URL Test, фильтры.
- **PASS по общему отчёту без деталей:** D2, D5, D6, I1–I4, K1, K3, K4, K6,
  E4, H8 («остальные оставшиеся сценарии тоже прошли»).
- **Не подтверждено:** F1–F4 чек-листа (трей/автозапуск/смена портов — номера
  в отчёте относились к языку), M5, A (VS2010), G, не перечисленные строки B–H.
- **Автотесты хоста (не XP):** 348/348, UI smoke 52/52 × 4, порт-тесты 8/8.

Исправление StopCore (таймаут 5 с) на XP не проверено и не может быть
проверено обычным сценарием (нужен «зависший» `xray.exe`).

### Windows 2000: первый опыт W0 (план; ничего не запускалось)

Цель — исходная точка на настоящей Windows 2000 SP4 до любой работы с
ядром/рантаймом Go: запускается ли оболочка .NET 2.0 и какой именно отказ
даёт загрузчик на `xray.exe`. Код, ядро и сборки не меняются. Процедура —
`docs/nt5-win2000.md`, раздел 5. Пересмотр после W0, до любых сборок.

## 2026-10-17: «portfix» — повторное подключение и порт 2081 (тестовый комплект, не выпуск)

### Причина (по данным XP) и исправление

- На XP после работы через системный прокси, Disconnect и подключения другого
  сервера: «Local HTTP port 2081 cannot be used: it is already used by another
  program». В момент ошибки `netstat -ano | findstr ":208"` — только `TIME_WAIT`
  на 127.0.0.1:2081, `LISTENING` нет; SOCKS 2080 проверяется первым и проходил,
  значит старый `xray.exe` порт не держал.
- Сообщение даёт только предварительная проверка `CoreLauncher.CheckPortFree`.
  Её пробный bind (`TryBind`) ставил `ExclusiveAddressUse = true`; на XP такой
  bind отказывает, пока у порта есть старые соединения браузеров в `TIME_WAIT`,
  хотя Xray (Go, обычный bind) порт открыл бы. На Windows 11 этот эффект не
  воспроизводится (проверено отдельной пробой) — исправность подтвердит только XP.
- Исправление: `ExclusiveAddressUse = false` в пробном bind. Настоящие
  конфликты по-прежнему находятся: слушатель на 0.0.0.0 — по таблице TCP
  (`TcpTable.IsListening`), слушатель или занятый bind на 127.0.0.1 — пробным
  bind, WSAEACCES — как раньше. Конфигурация слушателей Xray не менялась.
- Попутно (тот же путь остановки): `CoreController.StopCore` при таймауте 5 с
  забывал процесс и удалял `run\core.pid`, хотя `xray.exe` мог ещё работать —
  следующий запуск стартовал второе ядро рядом, очистка при запуске его не
  находила. Теперь процесс остаётся под контролем, pid-файл сохраняется,
  Stop/Start дают Error «The core process did not exit within 5 seconds.»
  (строка уже переведена) вместо второго ядра; следующий Stop/Start пробует
  снова; `Shutdown` удаляет pid-файл, только если ядро вышло. Автотеста нет:
  нужен процесс, переживающий `TerminateProcess` 5 с.
- Тесты: `TestPortCheckAfterDisconnect` (SelfTestMore: слушатель 127.0.0.1 и
  0.0.0.0, bind без listen, порт только с `FIN_WAIT_2`/`TIME_WAIT` — свободен;
  последние два различают старый/новый код только на XP), в
  `TestCoreLifecycle` — конфликт HTTP-порта через контроллер.

### Проверки (Windows 11 — хост, НЕ XP)

`build.cmd Debug` и `build.cmd Release` — 0 предупреждений, 0 ошибок;
`run-tests.cmd` — 348/348; `run-ui-smoke.cmd` Debug/Release × en/ru — 52/52
каждый. Комплект `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.6-rc2-portfix-test`:
48 файлов, `SHA256SUMS.txt` (LF) — `sha256sum -c` все OK; от `-rc2` отличается
только `NekoRayNT5.exe` (`d620d5b27b24b7a53bef83aaa92a7da0698748365724c5e39a90a2b44dd78d7c`);
версия в программе та же (0.9.6). Комплект `-rc2` не тронут.

### Состояние

- **A. Подтверждено пользователем на XP (rc2):** запуск и Diagnostics;
  VLESS + REALITY + Vision; импорт подписки в новую группу; подписка с
  Happ-совместимым HWID; URL Test и фильтры маршрутов. Остальные строки
  чек-листа этим не закрыты.
- **B. Только автотесты на хосте:** всё из «Проверки» выше, в т. ч. конфликт
  портов, переключение, Stop во время Connecting, восстановление прокси.
- **C. Нужна ручная проверка на XP (по порядку):** M1–M4 (порт 2081,
  повторные подключения, Exit/запуск, настоящий конфликт); D2, D5, D6
  (возврат прежних настроек прокси после Disconnect/Exit/аварии); I1–I4 (Add:
  обычный текст и списки); K1, K3, K4, K6 (меню вкладки группы, безопасное
  удаление); E4 и H8 (неудачное обновление подписки не трогает профили);
  L2–L4, L6 (переключение языка, 800×600).

## 2026-10-16 (ночь): 0.9.6 — TUN/TAP удалён, исправление Add, группы

Версия 0.9.6 (helper 1.2.0). Коммитов нет. Порядок: 1) удалить TUN ✅,
2) Add, 3) контекстное меню групп, 4) локализация, 5) Windows 2000.

### 1. TUN/TAP удалён (готово, проверено)

- Удалены: `Core/TunSession.cs`, `Platform/RouteTable.cs`,
  `Platform/TapAdapters.cs`; в `CoreController` — `ICoreAddOn`, `TunAddOn`,
  `TunActive`; флажок `checkTun`, пункты меню и трея TUN, `ProbeTun`/`SetTunMode`
  (`MainForm`); вкладка Settings → TUN; в Diagnostics — строки TAP/прав/TUN,
  кнопки Install TAP driver и Test adapter; `AppSettings.TunMode/TunAdapter/TunDns`.
- `XrayConfigBuilder`: правила direct (домены, Direct IP, частные сети) теперь
  применяются всегда (раньше выключались в режиме TUN).
- Старые настройки: `tunMode/tunAdapter/tunDns` игнорируются, исчезают при
  следующем сохранении. Новый `Core/LegacyCleanup.cs`: при запуске
  останавливает помощник TUN, оставленный 0.9.x (`run\tun.pid`, только при
  совпадении PID+времени+пути), удаляет `run\tun-state.xml`, пишет подсказку
  про маршруты 10.255.0.1 (маршруты не трогает: не постоянные).
- Помощник: удалены `engine.go`, `socks.go`, `tap_windows.go`, `tun_windows.go`
  и их тесты; команды `tun`, `tap-check`; `go.mod` без зависимостей, `go.sum`
  удалён. `nt5helper.exe` 6.4 → 4.7 МБ, только стандартная библиотека
  (`go version -m`). Удалены лицензии gVisor/x-sys/x-time/btree из `licenses/`
  (у `xray.exe` свои копии в `licenses/xray-modules`).
- Тесты: удалены TUN-разделы; новые `TestUpgradeFrom095` (0.9.5 с TUN on →
  без предупреждения, остальное сохранено, TUN-поля пропадают при сохранении,
  Direct IP в конфигурации) и `TestLegacyCleanup`; UI smoke: в меню/трее/панели
  нет TUN/TAP, Diagnostics без TUN.
- Результат: Debug 0 ошибок; self-tests 316/316; UI smoke 28/28 (Debug);
  Go-тесты ok.
- Документы: README (EN), `nt5-readme.ru.md`, архитектура (6.3 «удалён»),
  чек-лист (раздел G — обновление с 0.9.5), feature map, `dist/README.txt`,
  `runtime/README.txt`, `THIRD-PARTY.txt`, `third_party/README.md`.

### 2. Add: «Read 1 file», «•» → 3516 unsupported entries (готово, проверено)

- **Причина.** `AddSource.Classify` считал списком ссылок любой многострочный
  текст, а `ClipboardSuggestion` подставлял буфер, если в нём где-то есть
  `://`. Скопированная страница/чат (строка «Read 1 file», маркер «•» и тысячи
  строк, где-то адрес) уходила в `ShareLinks.ParseMany`, и **каждая** строка
  становилась записью «Link N: Unsupported link» → 3516 записей.
- **Исправление.** Новый `Services/LinkProblems.cs` вместо `List<string>`:
  ссылки с ошибкой считаются, подробно хранятся первые 20, причины
  группируются («Links 1, 2, 3, 4, 5 and 15 more: …», «… and N more»); строки,
  которые вообще не ссылки, только считаются («Lines that are not share links
  (ignored): N»). `ShareLinks.LinkStart`: ссылка — только если схема открывает
  строку, допускаются маркеры списка (•, ·, ●, –, —, `-`, `*`, `+`, `>`, `1.`,
  `2)`); `# vless://…` — комментарий, не импортируется; `http(s)://` внутри
  списка — понятная ошибка, а не загрузка. `Classify`: текст без `://`, без
  `%3A%2F%2F`, не Base64 и не JSON/YAML → сразу Invalid с объяснением.
  `AddSource.IsCleanSource`: из буфера при открытии Add подставляется только
  чистый источник (адрес, happ://, Base64, все строки — ссылки).
  Ничего не сохраняется до Add (как и раньше).
- Тесты: `TestAddClassification` (10 проверок: «Read 1 file •», страница из
  3516 строк, маркированный список, комментарий, 3000 битых ссылок, адрес в
  списке, JSON, буфер); UI smoke +2 (страница → одна строка объяснения, Add
  выключена). Результат: 128/128 в разделах Add/ссылки/подписки; UI smoke 30/30.

### 3. Контекстное меню вкладок групп (готово, проверено)

- Правый щелчок по вкладке группы (`MainForm.OnGroupTabsMouseUp`): вкладка
  сначала становится текущей, меню действует именно на неё. Пункты: New
  group..., Rename..., Update subscription (только подписка, не во время
  обновления), Use Happ-compatible HWID (только подписка), Manage groups...
  (прежнее окно Groups), Delete group... (с подтверждением: число серверов,
  предупреждение, если активное подключение из этой группы — оно
  останавливается). Default удалить нельзя (пункт выключен, `DeleteGroup(0)`
  отказывает); во время обновления подписки удаление выключено. Правый щелчок
  по пустому месту полосы вкладок — только New group.
- Новое окно `GroupNameForm` (только имя); Rename не меняет адрес, HWID и
  профили. Логика отделена от диалогов: `CreateGroup`, `RenameGroup`,
  `DeleteGroup`, `BuildGroupMenu` — проверяются UI smoke (+6).
- Diagnostics: кнопки Open link / Re-check / Copy report рядом (после удаления
  кнопок TAP оставался разрыв). Скриншоты `docs/screenshots/*.png`
  пересняты (без TUN).

### Итог блока 1–3 (Windows 11, не XP)

- Debug и Release: 0 ошибок, 0 предупреждений. Self-tests 326/326; UI smoke
  36/36 (Debug и Release); Go-тесты ok (с настоящим xray.exe).
- Комплект: `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.6` (48 файлов,
  `SHA256SUMS.txt` проверен; старые комплекты не тронуты). SHA-256:
  NekoRayNT5.exe `8d4f623b85dde734c8b4e3567e048125545710046005d2fa71e1fa615051a22f`,
  nt5helper.exe `cf942b9ecb0859eb9999311723f7059cb413ba8326f3702d398773257b950d66`,
  xray.exe `7592b7ac3f819bc017892fc6c617bba6c2f89b33b7394e733fb4037cd9322246` (без изменений),
  cacert.pem `a41b5d356aea97a529fe27e0f7316d2f9d946d75927476cf9cf1b90637d00505`.
- Поиск секретов по `nt5-ui`, документам и комплекту: только синтетические
  данные и GUID проектов.
- На XP пользователю: чек-лист G1–G4 (обновление с 0.9.5), H2–H9 (Add),
  C/D (подключение и системный прокси), правый щелчок по вкладкам групп.

### 4. Локализация English/Русский (готово 2026-10-17, проверено на Windows 11)

- Таблица `Lang/ru.txt`: 608 текстов (вся видимая часть программы, включая
  сообщения сервисов, ядра, системного прокси, хранилищ, окна сбоя).
  Не переводятся: протокольные значения (proxy/direct, AsIs, tls...),
  данные пользователя, вывод ядра `[core]`, файл `logs\nekoray-nt5.log` и
  отчёт «Copy diagnostic info» (стабильный английский формат для поддержки).
  «Default» переводится только у встроенной группы (id 0) с этим именем.
- Проверка покрытия `TestTranslation` (self-test; исходники через `NT5_SRC`):
  токенизатор C#, склейка литералов через `+`, каждая ветка `?:` — отдельный
  ключ, неконстантный ключ — ошибка; нет пропущенных, лишних, дубликатов,
  расхождений `{n}`, непереведённых длинных строк. Независимый
  скрипт-экстрактор дал те же 608 ключей.
- UI smoke: параметр языка (`run-ui-smoke.cmd xray Debug|Release en|ru`) и
  проверки раскладки всех окон (контрол внутри родителя, окно ≤ 800×600,
  кнопки/подписи фиксированного размера вмещают текст, подпись не наезжает на
  поле, панель инструментов без переполнения). В русском найдены и исправлены:
  подпись HWID наезжала на поле (Settings), узкие кнопки «Открыть файл...»
  (Add) и «Открыть ссылку» (Diagnostics), обрезанная подпись HWID в Add,
  обрезанный заголовок «Состояние».
- Смена языка показывает сообщение «изменится при следующем запуске»;
  смена только не относящихся к ядру настроек больше не перезапускает
  подключение (`MainForm.ConnectionSettingsChanged`).
- `tests/make-screenshots.cmd [папка] [en|ru]` — снимки всех окон для
  проверки (по умолчанию `%TEMP%\nekoray-nt5-review`, не в docs).

### Итог фаз 1–2 (Windows 11 x64 — хост, НЕ XP)

Команды: `MSBuild NekoRayNT5.sln /p:Configuration=Debug|Release /p:Platform=x86`;
`tests\run-tests.cmd <Release\xray.exe>`;
`tests\run-ui-smoke.cmd <xray.exe> Debug|Release en|ru`;
`set XRAY_EXE=<xray.exe>` + `helper\build.cmd test`.
- Debug и Release: 0 ошибок, 0 предупреждений.
- Self-tests: 342 passed, 0 failed, 0 skipped.
- UI smoke: 52/52 в каждом из 4 прогонов.
- Go-тесты помощника: ok.
- Аудит фазы 1: TUN/TAP отсутствует в UI, настройках, рантайме, помощнике,
  проекте; остались только `LegacyCleanup`, пропуск старых полей в
  `SettingsStore` и тесты обновления. `helper/go.mod` без зависимостей,
  `go.sum` нет, импорты — только стандартная библиотека (`go version -m`).

### Фаза 3. Готовность к XP (документы; на XP НЕ проверялось)

- `docs/nt5-test-checklist.md`: версия 0.9.6, путь к комплекту, предусловия
  XP, что записывать при сбое; новые разделы I (Add: обычный текст, списки,
  буфер), K (меню групп), L (язык, 800×600); G — обновление с 0.9.5.
- Автоматически на хосте покрыто: профили и хранение, импорт (ссылки,
  списки, файлы, подписки по локальному HTTP/HTTPS через настоящий
  nt5helper), меню групп, обновление настроек 0.9.x, раскладка окон ≤ 800×600
  на обоих языках. Подключение VLESS+REALITY+Vision и настоящий системный
  прокси на XP — **ожидают ручной проверки** (C, D, E, G, I, K, L).

### Фаза 4. Windows 2000 (анализ; на Windows 2000 НЕ проверялось)

- `docs/nt5-win2000.md`: матрица, PE-аудит (`xray.exe`/`nt5helper.exe`:
  подсистема 5.1, статический `AddVectoredExceptionHandler`;
  `NekoRayNT5.exe`: подсистема 4.0, только `mscoree.dll`), P/Invoke оболочки
  (всё есть в NT 5.0), динамические зависимости рантайма Go (VEH,
  RtlGenRandom, ConnectEx, GetAddrInfoW — все XP+), варианты (порт рантайма
  на NT 5.0, неофициальное расширенное ядро, шлюз через XP в локальной сети),
  план W1–W9.
- **Блокер:** подключение на Windows 2000 невозможно без ядра для NT 5.0.
  Отдельной ветки/форка не создавалось; сообщение Diagnostics для Windows 2000
  уточнено (не утверждает, что окно работает).

### Фаза 5. Приватность, лицензии, репозиторий

- Git: ветка `nt5/audit`, 412 коммитов, все — upstream Nekoray (авторы
  проекта Nekoray); коммитов NT5 нет, `nt5-ui/` и `docs/nt5-*` не
  отслеживаются; stash пуст. Исторической утечки данных NT5 нет.
- Поиск секретов по `nt5-ui`, `docs`, комплекту: только синтетические
  данные, GUID проектов и синтетический GUID теста. Имени пользователя и
  локальных путей в файлах проекта нет (убраны пути лаборатории из
  `helper/build.cmd`: по умолчанию `nt5-ui\toolchain\go`, добавлен в
  `.gitignore`).
- **Происхождение Go-тулчейна установлено:** архив совпадает побайтно
  (размер 244 696 306, SHA-256 `34b9c70b...9044`) с ассетом `go-386.zip`
  выпуска `go1.20-nt51` (тег `2023_02_12_120`) репозитория
  https://github.com/BieHDC/go-backports (ветка `release-branch.go1.20-nt51`).
  Скачан во временную папку только для сравнения хэша и удалён.
  `third_party/README.md`, README, `build.cmd` обновлены.
- Лицензии: `THIRD-PARTY.txt` соответствует комплекту (помощник —
  только стандартная библиотека Go; лицензии gVisor/x-sys/x-time/btree для
  помощника убраны, у `xray.exe` свои копии в `licenses/xray-modules`; TAP
  убран). `wireguard-go` (MIT) — лицензия в `xray-modules`, изменение —
  патч в `third_party`.
- **Блокер публичного бинарного выпуска (остаётся):** опубликовать полный
  соответствующий исходный код `xray.exe` (коммит MatsuriDayo/Xray-core
  `f6843e4e...`, оба патча, исходники модулей из его `go.sum`, параметры
  сборки). Исходники самой NekoRay NT5 публиковать можно.
- README (EN) переписан: что делает и чего нет, порядок работы,
  «System Proxy — не VPN», таблица ОС (Windows 2000 — не поддерживается),
  сборка (тулчейн), тесты, лицензии. Русский README и `dist/README.txt`
  согласованы. Снимки Windows 11 удалены из `docs/screenshots`;
  `docs/nt5-screenshots.md` — список снимков для XP/2000 (имена файлов,
  язык, экран, синтетические данные).

### Фаза 6. Финальный прогон и комплект (Windows 11, хост)

- После всех изменений кода: Debug/Release 0 ошибок; self-tests 342/342;
  UI smoke 52/52 × (Debug, Release) × (en, ru); Go-тесты ok; проверка
  целостности исходников — нет не-ASCII в коде, нет смешанных переводов
  строк (оставшиеся «braces» — скобки в строковых литералах).
- Комплект-кандидат: `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.6-rc2`
  (48 файлов, `SHA256SUMS.txt` проверен `sha256sum -c`). SHA-256:
  NekoRayNT5.exe `2a8f1e04551c5867b25e240ddb7fd6745065f76f32593329b2f29fbf9a72e763`,
  nt5helper.exe `cf942b9ecb0859eb9999311723f7059cb413ba8326f3702d398773257b950d66`,
  xray.exe `7592b7ac3f819bc017892fc6c617bba6c2f89b33b7394e733fb4037cd9322246`,
  cacert.pem `a41b5d356aea97a529fe27e0f7316d2f9d946d75927476cf9cf1b90637d00505`.
  Содержимое: exe ×3, cacert.pem, README.txt, licenses\ — без профилей,
  настроек, логов, TUN/TAP. Прежний `NekoRayNT5-v0.9.6` (без перевода)
  устарел и оставлен нетронутым.

### Следующие шаги (по приоритету)

1. Пользователь: прогнать на XP-VM чек-лист `docs/nt5-test-checklist.md`
   (C, D, E, G, I, K, L) с комплектом `-rc2`; прислать результаты.
2. Пользователь: снимки по `docs/nt5-screenshots.md` → обновить README.
3. Пользователь (при наличии VM): Windows 2000 W1–W9 (`docs/nt5-win2000.md`).
4. Решение пользователя: делать ли порт рантайма Go на NT 5.0 (большая
   отдельная задача) или оставить Windows 2000 неподдерживаемой.
5. Перед публичным выпуском: опубликовать исходники `xray.exe`; решить,
   какой README будет корневым; сборка в VS2010 SP1 (F5, Release).

Ниже — промежуточная запись этапа 4 (история):

Сделано (Debug собирается, 0 ошибок; тесты после этого НЕ перезапускались):
- Механизм: `App/Lang.cs` (`Lang.T("English")`, `Lang.F("... {0}", x)`;
  таблица `NekoRayNT5/Lang/ru.txt`, UTF-8, пары строк `en: ` / `ru: `,
  встроена как EmbeddedResource `NekoRayNT5.Lang.ru.txt`; нет перевода →
  английский). `AppSettings.Language` ("en" по умолчанию), `SettingsStore`
  (`language`), выбор в Settings → General (применяется при следующем запуске),
  `Program.ApplyLanguage()` до создания окна. `run-tests.cmd`: `/resource:` и
  `NT5_SRC` (для будущей проверки покрытия). `RowLayout`: колонка подписей
  расширяется под длинные подписи, длинные Caption переносятся.
- Обёрнуты в Lang.T/F: SettingsForm, ProfileEditForm, GroupsForm
  (+GroupNameForm), AddForm, DiagnosticsForm (+FirstRunForm), UiHelpers
  (Cancel), MainForm (меню, трей, панель, статусы, журнал, диалоги; тексты
  дизайнера — после InitializeComponent; «Default» переводится только как
  стандартное имя), ShareLinks и ProfileValidator (простые сообщения через
  временный скрипт). Отчёт «Copy diagnostic info» и `DiagLog` намеренно
  остаются на английском (для поддержки).
- `Lang/ru.txt` пока только заголовок — переводов НЕТ (позже заполнено).

Шаги, выполненные 2026-10-17:
1. Проверить ProfileValidator: обёрнуто 29 `return Lang.T(...)` — убедиться,
   что нет обёрнутых значений, не являющихся сообщениями.
2. ShareLinks вручную: сообщения с конкатенацией (строки ~179-180, 204, 209,
   214, 420, 615) → `Lang.F`.
3. Остальные сервисы: AddSource (Describe, Problem, HwidNotice,
   DescribeUserInfo), LinkProblems.Summarize, Subscription,
   SubscriptionClient (DescribeRoute/Via/SafeMessage), Dependencies, CaBundle,
   CoreLauncher/CoreController, ExecutableCheck, SystemProxyManager, Hwid,
   LatencyTester, ProfileStore (предупреждения, MergeResult.ToString),
   SettingsStore, LegacyCleanup, окна сбоя в Program.cs.
4. Скрипт-экстрактор ключей (Lang.T/F, склейка литералов через `+`, ветки
   `?:`) → заполнить `Lang/ru.txt` переводом; self-test: разбор таблицы,
   совпадение `{n}`, покрытие всех ключей из исходников (через `NT5_SRC`).
5. RU-скриншоты всех окон (Screenshots.cs с параметром языка) и проверка
   800×600; UI smoke на ru.
6. Полный прогон: Debug/Release, self-tests, UI smoke ×2, Go; новый комплект.

## 2026-10-16 (вечер): стабилизация 0.9.5 — диагностика TUN, лицензии, приватность, GitHub

Версия остаётся 0.9.5 (helper 1.1.0). Коммитов нет.

### TUN на XP: «TUN mode: the helper exited during start-up.»

- **Найденный дефект (C#).** `TunHelperProcess.Start` замечал выход помощника
  (`HasExited`) раньше, чем асинхронный читатель доставлял его строку
  `ERROR ...` или отчёт о сбое Go в stderr, — причина подменялась общей фразой.
  Теперь: `WaitForExit()` без тайм-аута (ждёт конца обоих потоков), хранятся
  последний шаг и последние 12 строк; сообщение: шаг, код выхода, причина
  помощника или строка `panic:`/`fatal error:`, следующее действие по шагу.
  Регрессия: 5 прогонов настоящего `nt5helper tun` с несуществующим адаптером —
  всегда `step "open" (exit code 3) ... error 2`.
- **Помощник.** Строки `STAGE open/version/mtu/config-tun/dhcp/dns/engine/media`;
  ошибки Windows с номером (`error N`); паника главного и рабочих потоков →
  `ERROR internal error ...`; устройство за интерфейсом (`openDevice`), чтобы
  тесты прогоняли всю последовательность без драйвера; команда `tap-check`
  (открыть адаптер, GET_VERSION, GET_MTU — ничего не меняет).
  Go-тесты: вся последовательность до READY и STOP на поддельном устройстве,
  отказ на каждом шаге (кабель всегда отключается), `tap-check` успех/отказ,
  реальный CreateFile на несуществующий адаптер.
- **Diagnostics → Test adapter** (фоновый поток) и разбор ответа
  `ParseAdapterCheck` (успех, отказ, нет ответа) — с тестами.
- **Совместимость с XP (что можно проверить без XP):** PE обоих Go-файлов —
  подсистема 5.1, статический импорт только `kernel32.dll`, одинаковый у
  `nt5helper.exe` и проверенного на XP `xray.exe`; помощник не ссылается ни на
  одну функцию Vista+, которой нет в `xray.exe`. Значит, падение при загрузке
  маловероятно; вероятнее отказ устройства/драйвера, чью причину скрывала гонка.
  **Первопричина на XP не установлена — нужен прогон G1b–G1d чек-листа.**
- `GO386=sse2` у `xray.exe` и `nt5helper.exe`: нужен процессор с SSE2
  (в требованиях README).

### Лицензии и исходники бинарников

- `xray.exe`: MatsuriDayo/Xray-core `f6843e4e…` (1.8.3), изменён только
  `go.mod` (replace на `wireguard-go-nt5`). Форк wireguard-go = upstream
  `c99467f53f2c` + одна строка (`ipc/uapi_windows.go`: убрана метка
  целостности `S:(ML;;NWNRNX;;;HI)` — на XP разбор SDDL падает → panic при
  инициализации). Патчи и происхождение: `nt5-ui/third_party/`.
- Модули из встроенной в `xray.exe` информации (36): тексты лицензий и NOTICE
  (grpc, yaml.v2) собраны в `nt5-ui/licenses/xray-modules/` из кэша модулей,
  `INDEX.txt` — список. GPL-3.0-or-later: `sagernet/sing`, `sing-shadowsocks` →
  `xray.exe` распространяется на условиях GPL-3.0 с полным исходным кодом
  (наше прочтение, не юридическое заключение).
- Toolchain Go 1.20 с бэкпортами XP: архив `go-386.zip`
  (SHA-256 `34b9c70b…9044`); **публичный репозиторий бэкпорта не установлен**.
- **Блокер публикации бинарного релиза:** выложить исходники/патчи из
  `third_party/README.md` и назвать источник toolchain. Исходники NekoRay NT5
  публиковать можно.

### Исправления и улучшения

| Что | Почему |
|---|---|
| Тесты задержки: сброс «идёт тест» в `finally` | Исключение оставляло `_testRunning` навсегда — все следующие тесты отклонялись |
| Адрес подписки — в файле (`-url-file`, curl `--config`) | Токен был виден в командной строке процессов |
| Очистка `run\subscription-*` при запуске | После сбоя оставались файлы с HWID/ответами |
| Отчёт диагностики: `%APPDATA%`/`%USERPROFILE%` вместо путей | В отчёт попадало имя пользователя Windows |
| Release без `.pdb` (`DebugType none`) | В `NekoRayNT5.exe` был записан локальный путь сборки |
| Add: повторный адрес подписки → обновить существующую группу | Создавалась вторая группа с теми же серверами |
| Add: перевод строк LF → CRLF в поле | Список из Linux выглядел одной строкой |
| System Proxy во время Connecting | Флажок был недоступен; теперь выбор запоминается и применяется после подключения |
| Ошибка включения прокси | Объясняет, что настройки не изменены, и предлагает локальные порты |
| Go-тест `TestThroughRealXray` | Порт из динамического диапазона → WSAEACCES для UDP; теперь 20000–44999, TCP+UDP, ≤60 попыток |
| Diagnostics: точная ссылка `tap-windows-9.9.2_3.exe` | Была ссылка на каталог |
| Невидимые U+FEFF в литералах, кириллица в тесте | Заменены на `﻿`, `В…` (csc 2.0 читает файлы без BOM в ANSI) |
| Тексты для пользователя без «VM»/лаборатории | Требование публичности |

Новые тесты: TestTunHelperDiagnostics, TestTempCleanup, TestUpgradeFrom091
(настройки и профили 0.9.1), curl-ветка (системный `curl.exe` Windows 11,
адрес из `--config`, сертификат проверяется), Go: tun_sequence_test.go,
tun_test.go, TestFetchURLFromFile.

### Итоговые проверки (Windows 11; XP, Win2000 и VS2010 — не проверялись)

- MSBuild 4.0 Debug/Release x86 — 0 ошибок, 0 предупреждений.
- `run-tests.cmd` — 360/360; `run-ui-smoke.cmd` Debug 28/28, Release 28/28;
  `helper\build.cmd test` — ok (3 прогона подряд), `go vet` и `gofmt` чисто.
- После тестов процессов xray/nt5helper/NekoRayNT5 не осталось.
- Комплект `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.5`: 52 файла,
  `SHA256SUMS.txt` (LF, проверяется `sha256sum -c`) — все OK; в исполняемых
  файлах нет локальных путей и имени пользователя.

### Действия только на реальной XP (по чек-листу)

1. Diagnostics → TAP → Test adapter (G1b) и `nt5helper.exe tap-check` (G1d).
2. TUN: G1c (полный текст ошибки при отказе), затем G4–G10 и G5a (утечки).
3. HTTPS-подписка с `cacert.pem` 0.9.5, Add/HWID (раздел H).
4. VS2010 SP1: открыть решение, F5, Release (раздел A).

### GitHub

- `nt5-ui/README.md` — английский README проекта (возможности, требования,
  сборка, тесты, проверенное/непроверенное, лицензии); прежний русский —
  `docs/nt5-readme.ru.md`. Корневой `README.md` — оригинал Nekoray, не менялся
  (решение о главной странице репозитория — за владельцем).
- Скриншоты `docs/screenshots/*.png` — `tests/make-screenshots.cmd`
  (настоящая программа, синтетические данные, Windows 11).
- Аудит секретов (рабочее дерево, документы, комплект): реальных UUID, ключей,
  адресов, токенов, HWID нет. История Git — только коммиты Nekoray (HEAD «3.17»).

## Состояние на 2026-10-16: версия 0.9.5 (кандидат 1.0: единый Add, HWID, маршруты подписок, диагностика)

Номер 1.0.0 — после ручной приёмки на XP (раздел «Осталось»). Коммитов нет:
`nt5-ui/` и `docs/nt5-*.md` по-прежнему не в Git.

| Блок | Что сделано | Файлы |
|---|---|---|
| Порты и тесты | `FindFreePort`: случайный порт 20000–44999 (ниже динамического диапазона, который Windows раздаёт исходящим соединениям в любой момент), проверка TCP **и** UDP, без повторов, `IOException`, если не нашлось; WSAEACCES объясняется (исключённые диапазоны, `netsh ... excludedportrange`), не скрывается; `CoreLaunchException.PortProblem` + до 3 попыток в URL Test; при подключении SOCKS-порт проверяется и по UDP. Причина сбоев 0.9.1 «connected» в тесте — порты из динамического диапазона (515xx). UI-смоук: сторожевой поток закрывает любое модальное окно (#32770) с FAIL и его текстом, общий срок 240 с — больше не зависает | `Core/CoreLauncher.cs`, `Core/CoreController.cs`, `Services/LatencyTester.cs`, `tests/UiSmoke.cs` |
| Единый Add | Одна команда (панель, Ctrl+N, Server, Subscription): ссылки (одна/много, Base64, %-кодированные), файл, `http(s)://`, `happ://add/<url>`. `happ://crypt*` — честный отказ (декодера нет). Адрес сначала скачивается и разбирается, предпросмотр: имя (`profile-title`, в т. ч. `base64:`; `content-disposition`; хост), число серверов, дубликаты в источнике и уже имеющиеся, ошибочные строки, трафик/срок (`subscription-userinfo`). JSON/Clash — понятный отказ. «Keep as a subscription» по умолчанию, если похоже на подписку; иначе разовый импорт (в текущую группу или Default). Подписка добавляется из проверенного содержимого без второй загрузки | `Services/AddSource.cs`, `UI/AddForm.cs`, `UI/MainForm.cs` (удалены `LinkInputForm*`) |
| HWID | Случайные 16 байт (hex) один раз на установку, `settings.xml`; можно задать свой (проверка символов/длины), «Make a new HWID». Заголовки `x-hwid`, `x-device-os`, `x-ver-os`, `x-device-model`; User-Agent остаётся `NekoRayNT5/<версия>` (не выдаём себя за Happ). Флажок предлагается только для адресов, отмечен для `happ://add`; у подписки — переключатель Server → Subscription → Use Happ-compatible HWID. В журналы/диагностику HWID не попадает; помощнику передаётся файлом, а не в командной строке. Ответные `x-hwid-*` объясняются (лимит устройств и т. п.) | `Services/Hwid.cs`, `Model/*`, `Services/SettingsStore.cs`, `Services/ProfileStore.cs` |
| Загрузка подписок | `SubscriptionClient`: Auto / Only through the active connection / Direct (Settings → Subscriptions). Auto: через ядро, при сбое *до ответа сервера* — один раз напрямую; ответ сервера (4xx, мусор) не повторяется. Ни один загрузчик не использует системный прокси (петли нет). Все три загрузчика: заголовки, gzip/deflate, белый список ответных заголовков, `FetchException.ServerAnswered`. `nt5helper fetch`: `-headers FILE`, `-meta FILE`, `-agent` (запрет Host и hop-by-hop), версия 1.1.0. Неудача — профили не меняются, причина и время последнего успеха в строке состояния, там же трафик/срок и «HWID on» | `Services/Subscription.cs`, `Services/SubscriptionClient.cs`, `helper/fetch.go`, `helper/main.go` |
| Диагностика и TUN | `Dependencies.Check`: Windows, xray, nt5helper, cacert.pem (возраст), curl, порты, TAP-драйвер, права, TUN. Окно Diagnostics (панель, Preferences): причина и следующий шаг, «Open page» (официальные curl.se / build.openvpn.net), «Install TAP driver...» — только выбранный пользователем файл, подтверждение с предупреждением о версии, `runas`; ничего не скачивается. Щелчок по недоступному TUN открывает это окно вместо сообщения | `Services/Dependencies.cs`, `UI/DiagnosticsForm.cs` |
| Интерфейс | Панель: Add / Connect↔Disconnect / Update subscriptions / Settings / Diagnostics. Мастер первого запуска (без профилей): файлы → Add → готовность → Connect. Настройки: General, Connections, Subscriptions, Routing & DNS (прокрутка, влезает в 800×600), TUN, Diagnostics | `UI/MainForm.cs`, `UI/SettingsForm.cs`, `UI/DiagnosticsForm.cs` (`FirstRunForm`) |
| Комплект | `licenses/` (GPL-3.0, MPL-2.0 Xray и cacert, BSD Go/x/sys/x/time, Apache gVisor/btree, `THIRD-PARTY.txt`), `dist/README.txt`; Release кладёт их в `package\` | `NekoRayNT5.csproj` |

Проверки (Windows 11, без VS2010): MSBuild 4.0 Debug/Release x86 — 0 ошибок,
0 предупреждений; `run-tests.cmd` — 341/341 (было 292; новые разделы
TestAddSource, TestDownloadRoutes, TestPorts + HWID/маршруты через настоящий
`nt5helper` по TLS); `run-ui-smoke.cmd` Debug и Release — 28/28 (Add, Diagnostics,
панель, «нет неожиданных окон»); `helper\build.cmd test` — ok (16 Go-тестов,
новые: заголовки/мета, gzip). Выборочный запуск: `run-tests.cmd xray.exe TestA TestB`.
Замечание: встроенный клиент .NET 2.0 не ведёт запросы к 127.0.0.1 через прокси —
маршруты Auto/Proxy проверяются через `nt5helper`.

Комплект: `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.5` (+ `SHA256SUMS.txt`).

### Следующие шаги

1. XP-VM: чек-лист, раздел H (Add/HWID/маршруты) и A–F; HTTPS-подписка Quattro.
2. TUN с драйвером TAP 9.9.2_3 на XP-VM (G0–G10) — без этого TUN не заявляется.
3. VS2010 SP1: открыть, F5, Release (A2a, A2b).
4. Перед публикацией: тексты лицензий зависимостей Xray (go.mod 1.8.3) в `licenses/`.
5. Косметический этап (ниже).

## Состояние на 2026-10-15: версия 0.9.1 (доводка после первой проверки 0.9.0 на XP)

Результат пользователя на XP с 0.9.0: VLESS + Reality + Vision через System Proxy
работает; подписка Quattro — `fetch: the server certificate is not trusted
(unknown certificate authority)`; флажок TUN Mode серый; в VS2010 в Output —
`System.Security.SecurityException` в `mscorlib.dll`, выход с кодом 0.

| Проблема | Первопричина | Исправление |
|---|---|---|
| HTTPS-подписка на XP: unknown certificate authority | В пакете 0.9.0 не было `cacert.pem`; `nt5helper` искал его только рядом с собой и без него проверял цепочку по хранилищу Windows XP, где нет современных корневых (ISRG Root X1 и др.). `cacert.pem` был помечен в проекте как необязательный, `runtime/` целиком в `.gitignore` | `runtime/cacert.pem` (Mozilla, данные от 2026-09-25, 121 сертификат, SHA-256 сверен с curl.se) хранится в Git; MSBuild копирует его по имени, предупреждает об отсутствии; Release собирает `package\`. Приложение передаёт `-ca` явно, заранее проверяет файл (нет / повреждён / пуст → понятное сообщение без обращения к серверу). `nt5helper` читает пакет строго (повреждённый блок = ошибка, код 8), сообщение о недоверии называет источник (файл или хранилище Windows). Адреса вырезаются из любого вывода загрузчиков |
| TUN Mode серый на XP | `UpdateUi` отключал флажок и пункты меню при любой причине недоступности, а объяснение показывалось только по щелчку — который был невозможен. Запись в журнал подавлялась, если адаптеров нет и TUN не выбран. Фактическая причина на VM — драйвер TAP не установлен | Флажок активен всегда (кроме Connecting/Stopping); подсказка с причиной; щелчок — окно с причиной и порядком установки; причина всегда в журнале при запуске. Тексты: какой файл драйвера, почему не 9.21+, что делать с правами, где искать `nt5helper.exe` |
| `SecurityException` в Output VS2010 | Исключение первого шанса, обработанное: `TapAdapters.Find` открывал все подключи `...\Class\{4D36E972-...}`, включая `Properties`, читать который может только SYSTEM; `OpenSubKey` бросал, `catch` поглощал. Стек: `RegistryKey.OpenSubKey ← TapAdapters.Find ← MainForm.ProbeTun ← MainForm.Startup` (и `SettingsForm..ctor`). Код выхода 0 — штатное завершение | Открываются только подключи-адаптеры `0000`…`9999`. Проверено перехватом исключений первого шанса (хост CLR 4 с `AppDomain.FirstChanceException`): было 2, стало 0 — и в проверке окна, и в настоящем `Program.Main` |
| Проверка окна иногда падала на Windows 11 | `FindFreePort` проверял только TCP; порт попадал в зарезервированный Windows диапазон UDP (Hyper-V/NAT), Xray не мог открыть UDP SOCKS. Тот же метод использует URL Test | Порт проверяется и по UDP |

Проверки 0.9.1 (Windows 11): MSBuild 4.0 Debug/Release — 0 предупреждений;
`run-tests.cmd` 292/292 в 21 из 23 прогонов; в 2 прогонах — сбой
«connected» в разделе Connect requests: ядро не запустилось, все три случайных
порта (51501/51558/51578) лежали в зарезервированных Windows 11 диапазонах UDP,
хотя `FindFreePort` теперь проверяет UDP. Причина до конца не установлена (за
10 прогонов подряд с подробным сообщением не повторилось); обычное подключение
не затронуто — оно использует настроенные порты (новое: `cacert.pem`, сквозная HTTPS-загрузка через
настоящий `nt5helper.exe` с локального TLS-сервера на Xray: доверенный CA,
недоверенный, повреждённый пакет, отсутствие пакета); `run-ui-smoke.cmd` 22/22
(6 прогонов подряд); Go 14/14; ручной `nt5helper fetch -ca cacert.pem` к трём
публичным HTTPS-серверам — цепочки проверены без хранилища Windows.

На XP не проверено: HTTPS-подписка Quattro с 0.9.1, TUN с драйвером (G0–G10),
VS2010 (A2a, A2b). Пакет: `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.1`.

## Состояние на 2026-10-14: версия 0.9.0

### Найденные причины ошибок

| Проблема | Причина | Исправление |
|---|---|---|
| Падение 0.3.0 на Win2K после `Profiles loaded: 0` | `ProbeCore` запускал `xray.exe version` в фоновом потоке без перехвата; `xray.exe` — подсистема PE 5.1 + импорт `AddVectoredExceptionHandler` → `Win32Exception` 193 → .NET 2.0 завершает процесс | `ExecutableCheck`, перехват во всех фоновых задачах, глобальные обработчики, `crash.log` |
| Двойной щелчок открывал редактор | `DoubleClick += OnEditClick` | Единый `StartProfile`; F2 — редактор |
| Повторный щелчок / гонки при переключении | Состояние Connecting ставилось из рабочего потока; отменённый запуск перетирал состояние нового | Connecting синхронно, `StartResult.AlreadyActive`, отмена без смены состояния |
| Подписки «только сохраняются» | Не было «добавить и загрузить»; обновление заменяло группу новыми id; HTTPS на XP — TLS 1.0 | Add subscription, слияние со стабильными id, `nt5helper fetch` |
| Ошибка сети для `http://`-подписки выходила как `WebException` | Не преобразовывалась | Понятный текст |
| Дубликат id в `profiles.xml` терял профиль | Запись пропускалась | Переназначение id |
| Повреждённый `profiles.xml` давал пустой список | `.bak` не использовался | Загрузка `.bak` |
| Проверка порта не видела слушателя на 0.0.0.0 | Только пробный bind | `GetTcpTable` |
| PID-файл мог указать на чужой процесс | Только PID | PID + время запуска + путь |
| TUN в 0.2.0 | Флажок дизайнера без кода | Реализация TAP-Windows + nt5helper |

### Файлы

Новые: `App/DiagLog.cs`, `Core/ExecutableCheck.cs`, `Core/ChildPidFile.cs`,
`Core/TunSession.cs`, `Platform/TcpTable.cs`, `Platform/RouteTable.cs`,
`Platform/TapAdapters.cs`, `Services/ProfileKey.cs`, `helper/*` (Go),
`runtime/README.txt`, `tests/SelfTestMore.cs`, `tests/UiSmoke.cs`,
`tests/run-ui-smoke.cmd`.
Изменены: `Program.cs`, `Core/CoreController.cs`, `Core/CoreLauncher.cs`,
`Services/ProfileStore.cs`, `Services/Subscription.cs`,
`Services/SystemProxyManager.cs`, `Services/XrayConfigBuilder.cs`,
`Services/ShareLinks.cs`, `Services/SettingsStore.cs`, `Model/*`, `App/AppPaths.cs`,
`UI/MainForm.cs`, `UI/MainForm.Designer.cs`, `UI/GroupsForm.cs`,
`UI/SettingsForm.cs`, `NekoRayNT5.csproj`, `Properties/AssemblyInfo.cs`,
`tests/SelfTest.cs`, `tests/run-tests.cmd`, `.gitignore`, документы.

### Проверки (Windows 11)

- MSBuild 4.0: Debug/Release x86 — 0 ошибок, 0 предупреждений.
- `run-tests.cmd`: 276/276. `run-ui-smoke.cmd`: 22/22. `helper build.cmd test`: 11/11.

## Осталось (нужна фактическая среда)

1. Ручная приёмка на XP по `nt5-test-checklist.md` (разделы A–F), пакет
   `NekoRay-NT5-build-lab\versions\NekoRayNT5-v0.9.0`. После неё — версия 1.0.0.
2. TUN с настоящим драйвером TAP-Windows 9.9.x на XP-VM (раздел G).
3. VS2010 SP1: открыть, F5, Release (раздел A).
4. Windows 2000: нужна VM; ожидается работа оболочки без ядра (раздел W).
   Ядро для NT 5.0 — отдельная задача: бэкпорт Go-рантайма.
5. ~~`cacert.pem` для `nt5helper` на XP~~ — входит в пакет с 0.9.1;
   обновлять по `nt5-ui/runtime/README.txt`.

## Косметический этап (после приёмки)

- Подписи и единый стиль меню; иконки режима в строке состояния
  (цвет/текст: Not Running / Connecting / Running / Error; TUN / System Proxy).
- Список серверов: колонка «Group/Sub», выравнивание задержки, подсветка
  ошибок теста, запоминание ширины колонок и размеров окна.
- Окно настроек: таблица вместо ручной раскладки, подсказки к правилам.
- Тексты ошибок: единый формат «что случилось → что сделать».
- Перевод интерфейса (русский) — `translations/` оригинала как источник.
