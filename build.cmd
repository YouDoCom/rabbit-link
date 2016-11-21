@echo off
setlocal

md "%~dp0"

dotnet pack .\src\RabbitLink -o artifacts\

@if %errorlevel% neq 0 ( pause & exit /b %errorlevel%)

pause