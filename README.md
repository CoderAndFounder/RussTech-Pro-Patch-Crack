# RussTech Pro Patch

Патчер для установки **RUSS TECH**: включает Pro-функции без онлайн-лицензии и отключает автообновления (чтобы сервер не перезаписал патч).

## Что делает

| Файл | Патч |
|------|------|
| `RussTech.Core.dll` | `LicenseCodec.Verify` → всегда Pro; `UpdatePreferences` / `UpdateClient` → обновления отключены |
| `RussTech.Windows.dll` | `LicenseVault.Check` → всегда Pro |
| `RUSS TECH.dll` | `LicenseSession.IsPro`, `Refresh`, `RequireFeature` → всегда true; `UpdateCenterViewModel` без проверок/загрузок |

Оригиналы сохраняются рядом как `*.prepro`.

## Требования

- Windows x64
- .NET 9 Runtime **или** готовый `RussTechProPatch.exe` из папки `dist` (self-contained)

## Использование

1. Закройте RUSS TECH.
2. Запустите:

```bat
RussTechProPatch.exe "E:\iluma"
```

Без аргумента — патчер спросит путь к папке установки (там должны лежать `RUSS TECH.exe` и DLL).

3. Запустите `RUSS TECH.exe` из этой же папки.

## Сборка из исходников

```bat
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

Готовый exe: `dist\RussTechProPatch.exe`
