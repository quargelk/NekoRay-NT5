# NekoRay NT5 — список скриншотов для README

> **Сейчас (0.9.7-dev1):** в README используются снимки настоящей программы, сделанные на хосте
> Windows 11 с синтетическими данными (`docs/screenshots/`, подписаны как снимки Windows 11; снимок
> «подключено» — с локальным тестовым сервером 127.0.0.1). Снимки ниже, с Windows XP SP3 и Windows 2000
> SP4, ещё не сделаны; их нужно снять вручную и заменить ими снимки хоста.

Снимки делаются на **настоящей** Windows XP SP3 и Windows 2000 SP4 с версией-кандидатом
(сейчас — комплект `NekoRayNT5-v0.9.7-dev1-release`; готовит Release-сборка, см. `BUILDING.md`).
Снимки Windows 11 в README не используются.

## Общие правила (обязательно)

- Только синтетические данные: никаких настоящих серверов, UUID, ключей,
  адресов подписок, HWID, имени пользователя.
- Перед съёмкой: отдельный каталог данных. В `cmd`:
  `set NEKORAY_NT5_DATA_DIR=C:\nt5-shots` и запуск `NekoRayNT5.exe` из этого
  же окна `cmd` (настоящие профили в `%APPDATA%` не затрагиваются).
- Импортировать через Add (вставить целиком) эти синтетические ссылки:

```
vless://00000000-0000-4000-8000-000000000000@203.0.113.10:443?security=reality&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sni=www.example.com&fp=chrome&sid=01&flow=xtls-rprx-vision#Example VLESS Reality
vless://00000000-0000-4000-8000-000000000000@203.0.113.11:443?security=tls&type=ws&host=cdn.example.com&path=%2Fws&sni=cdn.example.com#Example VLESS WebSocket
trojan://example-password@198.51.100.20:443?security=tls&sni=trojan.example.net#Example Trojan
socks://198.51.100.40:1080#Example SOCKS5
```

- Тема XP — стандартная (Luna) или классическая, разрешение **800×600** или
  1024×768; снимок окна: Alt+PrintScreen, вставить в Paint, сохранить PNG.
- Не подключаться к этим адресам (это документационные адреса RFC 5737,
  подключение не заработает и не нужно). Для снимка «подключено» см. №4.
- Перед отправкой открыть каждый PNG и проверить, что в журнале окна нет
  путей с именем пользователя и настоящих адресов.

## Список

| № | Файл | ОС | Язык | Экран | Как получить |
|---|---|---|---|---|---|
| 1 | `main-window-en.png` | XP SP3 | English | Главное окно | После импорта выделить «Example VLESS Reality»; журнал внизу виден |
| 2 | `main-window-ru.png` | XP SP3 | Русский | То же | Settings → General → Language → Русский, перезапуск (из того же `cmd`) |
| 3 | `add-preview-en.png` | XP SP3 | English | Add с предпросмотром | Add → вставить ссылки выше + строку `not-a-link` → Check; не нажимать Add |
| 4 | `connected-system-proxy-en.png` | XP SP3 | English | Главное окно, состояние «Running», флажок System Proxy | Только если есть свой тестовый сервер: импортировать его на **отдельной** VM, переименовать в «Example server», замазать адрес в колонке Address и в журнале до сохранения; иначе снимок пропустить |
| 5 | `group-menu-en.png` | XP SP3 | English | Правый щелчок по вкладке группы (меню раскрыто) | Правый щелчок по вкладке Default → New group «Example group», затем правый щелчок по новой вкладке; снять, пока меню раскрыто |
| 6 | `settings-general-ru.png` | XP SP3 | Русский | Settings → Общие (видно поле «Язык») | Настройки... |
| 7 | `settings-subscriptions-en.png` | XP SP3 | English | Settings → Subscriptions | HWID на снимке маскирован программой (первые 6 знаков) — проверить |
| 8 | `diagnostics-en.png` | XP SP3 | English | Diagnostics | Toolbar → Diagnostics... |
| 9 | `diagnostics-w2k-en.png` | Windows 2000 SP4 | English | Diagnostics (строки Core / Helper) | Toolbar → Diagnostics...; лучше после сборки, где исправлен устаревший текст о KernelX (`docs/nt5-win2000.md`) |

Присланные снимки кладутся в `docs/screenshots/` с этими именами; README
(`nt5-ui/README.md`) обновляется ссылками на них.
