@echo off
setlocal

cd "%~dp0"

echo Restoring packages
dotnet restore .\src\RabbitLink
@if %errorlevel% neq 0 ( pause & exit /b %errorlevel%)

echo Building package
dotnet pack .\src\RabbitLink -o artifacts\ --configuration release
@if %errorlevel% neq 0 ( pause & exit /b %errorlevel%)

pause