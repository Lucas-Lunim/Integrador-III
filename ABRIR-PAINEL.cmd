@echo off
chcp 65001 > nul
cd /d "%~dp0"
python -B "%~dp0dashboard_local.py"
pause
