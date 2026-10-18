# NekoRay NT5 — карта функций Nekoray 3.17

Что есть в оригинале, где это лежит и в каком состоянии в NT5 (версия 0.9.1).

Обозначения:

- **Есть** — реализовано и покрыто автоматическими проверками или сценариями
  на машине разработки. Проверка на Windows XP — по `nt5-test-checklist.md`.
- **Есть, не проверено** — реализовано, но ни разу не выполнялось по-настоящему.
- **Нет** — отсутствует, причина указана.

## 1. Экраны

| Экран | Оригинал | NT5 |
|---|---|---|
| Главное окно | `ui/mainwindow.ui`, `ui/mainwindow.cpp` | Есть: `UI/MainForm` |
| Редактор профиля | `ui/edit/dialog_edit_profile.ui`, `ui/edit/edit_*.ui` | Есть: `UI/ProfileEditForm`, один диалог на все типы |
| Группы | `ui/dialog_manage_groups.ui`, `ui/edit/dialog_edit_group.ui` | Есть: `UI/GroupsForm` |
| Основные настройки | `ui/dialog_basic_settings.ui` | Есть частично: `UI/SettingsForm`, вкладка General |
| Маршрутизация и DNS | `ui/dialog_manage_routes.ui` | Есть: вкладки Routing и DNS, один набор правил |
| Значок в трее | `ui/mainwindow.cpp:241-261`, `ui/Icon.cpp` | Есть; значок рисуется в коде |
| Настройки Tun | `ui/dialog_vpn_settings.ui` | Нет: режим TUN удалён в 0.9.6 |
| Горячие клавиши | `ui/dialog_hotkey.ui` | Нет: отложено |
| Первый запуск | `ui/dialog_first_setup.ui` | Нет: ядро одно |

## 2. Главное окно

| Область | Оригинал | NT5 |
|---|---|---|
| Меню | Program, Preferences, Server | Есть; пункты без реализации убраны |
| Флажки | Tun Mode, System Proxy | System Proxy есть; Tun Mode нет (удалён в 0.9.6). Строка состояния показывает реальный режим |
| Поиск | поле `search` | Есть: по имени, адресу и типу |
| Вкладки групп | по вкладке на группу | Есть |
| Таблица | Type, Address, Name, Test Result, Traffic | Есть без Traffic; работающий профиль выделен жирным |
| Нижняя панель | Log, Connection | Log есть (300 строк); Connection убрана |
| Строка состояния | профиль, входящие, скорость | Состояние ядра, локальные порты, ход теста |

## 3. Меню

### Program

| Пункт | Оригинал | NT5 |
|---|---|---|
| Show Window | `mainwindow.cpp:261` | Есть (меню трея, двойной щелчок, повторный запуск программы) |
| Add profile from clipboard | `mainwindow.cpp:1123` | Есть |
| Start with system | `sys/AutoRun.cpp:33` | Есть, не проверено: значение в `HKCU\...\Run`, запуск с `-tray` |
| Remember last profile | `remember_id` в `NekoGui_DataStore.hpp` | Есть: при старте запускает последний профиль и возвращает системный прокси, если он был включён |
| Allow other devices to connect | адрес 0.0.0.0 | Есть |
| System Proxy: Enable / Disable | `mainwindow.cpp:723-756` | Есть, не проверено на настоящих настройках |
| Enable Tun | `mainwindow.cpp:758` | Нет: удалён в 0.9.6 (архитектура, 6.3) |
| Restart Proxy | — | Есть |
| Exit | `mainwindow.cpp:657` | Есть: останавливает ядро и восстанавливает прокси |
| Scan QR Code, Active Server, Active Routing, Restart Program | — | Нет |

### Preferences

| Пункт | NT5 |
|---|---|
| Groups | Есть |
| Basic Settings, Routing Settings | Есть: один диалог Settings |
| Open Config Folder | Есть: `%APPDATA%\NekoRayNT5` |
| Tun Settings | Нет (удалено в 0.9.6) |
| Hotkey Settings | Нет |
| Export/Import profile backup, Copy diagnostic info | Есть (новое) |

### Server

| Пункт | Оригинал | NT5 |
|---|---|---|
| New profile | `mainwindow.cpp:1118` | Есть: подменю по типам |
| Add profile from clipboard | `mainwindow.cpp:1123` | Есть: одна или несколько ссылок, также Base64-список |
| Add profile from link... | — | Есть |
| Import from file... | — | Есть |
| Start, Stop | `mainwindow_grpc.cpp:265`, `:388` | Есть |
| Edit | `mainwindow.cpp:1106` | Есть (пункт меню, F2, контекстное меню). Двойной щелчок и Enter подключают, как в оригинале |
| Clone | `mainwindow.cpp:1128` | Есть |
| Move | `mainwindow.cpp:1143` | Есть: Move Up, Move Down, Move to group |
| Select All, Delete | `mainwindow.cpp:1378`, `1167` | Есть; удаление с подтверждением |
| Share: Copy links | `mainwindow.cpp:1202` | Есть; плюс экспорт ссылок в файл. SOCKS — `socks://`; HTTP-профили ссылки не имеют |
| Share: Export config | `mainwindow.cpp:1228` | Есть: JSON для Xray в файл |
| Share: QR Code | `mainwindow.cpp:1256` | Нет |
| Current Group: Tcp Ping, Url Test | `mainwindow_grpc.cpp:60-257` | Есть; URL-тест запускает временное ядро и делает настоящий запрос |
| Current Group: Clear Test Result | `mainwindow.cpp:1369` | Есть |
| Current Group: Remove Duplicates | `mainwindow.cpp:1386` | Есть |
| Подписки: добавить, обновить, обновить все, импорт из файла | `mainwindow.cpp:1414` | Есть: меню Server → Subscription; слияние без потерь и дубликатов (архитектура, 6.2) |
| Сортировка | щелчок по заголовку | Есть: по имени, адресу, результату теста; порядок сохраняется |
| Copy to group | — | Есть (новое) |
| Контекстное меню списка | есть | Есть |
| Reset Traffic, Full Test, Resolve domain, Remove Unavailable, Debug Info, Neko Links | — | Нет |

## 4. Функции без собственного экрана

| Функция | Оригинал | NT5 |
|---|---|---|
| Запуск и контроль ядра | `sys/ExternalProcess.cpp` | Есть: `Core/CoreController`, `CoreLauncher`, `CoreProcess`, `JobObject` |
| Связь с ядром | gRPC, `rpc/gRPC.cpp` | Нет: заменена файлом конфигурации и перезапуском |
| Конфигурация Xray | `fmt/Bean2CoreObj_ray.cpp`, `db/ConfigBuilder.cpp` | Есть: `Services/XrayConfigBuilder`, проверяется `xray run -test` |
| Разбор и экспорт ссылок | `fmt/Link2Bean.cpp`, `fmt/Bean2Link.cpp` | Есть: `Services/ShareLinks` |
| Хранилище | `db/Database.cpp` (JSON) | Есть: XML, `Services/ProfileStore`, `SettingsStore` |
| Системный прокси | `QvProxyConfigurator.cpp` | Есть, не проверено на настоящих настройках; в отличие от оригинала восстанавливает исходные значения |
| Подписки | `sub/GroupUpdater.cpp` | Есть: списки ссылок (открытые, Base64, percent-encoded); HTTPS через nt5helper/curl. Clash и sing-box — нет |
| TUN | sing-box + wintun (Windows 7+) | Нет: реализация на TAP-Windows 9.9.x на XP не заработала и удалена в 0.9.6 |
| Результаты тестов | в памяти | Сохраняются с временем теста |
| Автоматическое обновление подписок | `sub_auto_update` | Нет |
| Маршрутизация и DNS | класс `Routing` | Есть: один набор правил. Наборы правил, свой JSON, DNS-объект — нет |
| Статистика трафика | RPC `QueryStats` | Нет: нужен API ядра |
| Список соединений | RPC `ListConnections` | Нет |
| Перезапуск ядра при падении | `ExternalProcess.cpp:106-139` | Нет автоматического перезапуска: состояние Error, прокси возвращается, пользователь уведомляется |
| Единственный экземпляр | `3rdparty/RunGuard.hpp` | Есть |
| QR-код | `3rdparty/qrcodegen.cpp` | Нет |
| Темы, переводы | `ui/ThemeManager.cpp`, `translations/` | Нет: строки на английском |
| Внешние ядра, hook.js, обновление программы, аварийный дамп | — | Нет |

## 5. Протоколы

| Протокол | Оригинал | NT5 |
|---|---|---|
| VLESS: tcp / ws / grpc / http, none / tls / reality, Vision | `fmt/TrojanVLESSBean.hpp`, `fmt/V2RayStreamSettings.hpp` | Есть |
| VMess (alterId 0) | `fmt/VMessBean.hpp` | Есть |
| Trojan | `fmt/TrojanVLESSBean.hpp` | Есть |
| Shadowsocks без плагинов | `fmt/ShadowSocksBean.hpp` | Есть |
| SOCKS5, HTTP | `fmt/SocksHttpBean.hpp` | Есть (без TLS); ссылки `socks://`, `socks5://` |
| Цепочка профилей, свой конфиг | `fmt/ChainBean.hpp`, `fmt/CustomBean.hpp` | Нет |
| TUIC, Hysteria, Naive | `fmt/QUICBean.hpp`, `fmt/NaiveBean.hpp` | Нет: другое ядро |

Все перечисленные как «Есть» конфигурации принимаются Xray 1.8.3
(`run -test`) — проверяется автоматически. Настоящее подключение подтверждено
пользователем только для VLESS + TCP + REALITY + Vision (оболочка 0.3.0 на XP);
версия 0.9.0 и остальные протоколы — ручная проверка по `nt5-test-checklist.md`.
