@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Building self-contained RussTechProPatch.exe ...
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist
if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)
echo.
echo OK: dist\RussTechProPatch.exe
echo Creating zip...
powershell -NoProfile -Command ^
  "Compress-Archive -Path 'dist\RussTechProPatch.exe','README.md','build.bat' -DestinationPath 'RussTechProPatch-share.zip' -Force"
echo OK: RussTechProPatch-share.zip
pause
