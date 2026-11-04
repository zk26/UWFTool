@echo off
chcp 936 >nul 2>&1
setlocal
title UWF 教师控制台 - 启用远程管理

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  [错误] 请右键选择“以管理员身份运行”本脚本。
  echo.
  pause
  exit /b 1
)

echo ============================================
echo  启用远程管理：%COMPUTERNAME%
echo ============================================
echo.

echo [1/5] 开启“文件和打印机共享”（教师机需要访问 \\计算机名\C$）...
netsh advfirewall firewall set rule group="文件和打印机共享" new enable=Yes >nul 2>&1
netsh advfirewall firewall set rule group="File and Printer Sharing" new enable=Yes >nul 2>&1
sc config LanmanServer start= auto >nul 2>&1
net start LanmanServer >nul 2>&1

echo [2/5] 开启 WMI（远程查询 UWF 状态、远程执行命令）...
netsh advfirewall firewall set rule group="Windows Management Instrumentation (WMI)" new enable=Yes >nul 2>&1
netsh advfirewall firewall set rule group="Windows Management Instrumentation (WMI-In)" new enable=Yes >nul 2>&1
sc config Winmgmt start= auto >nul 2>&1

echo [3/5] 允许远程管理员访问（工作组环境必需，域环境执行也无害）...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f >nul

echo [4/5] 允许 ping（教师机用来判断机器是否在线）...
netsh advfirewall firewall add rule name="UWF Console ICMP" protocol=icmpv4:8,any dir=in action=allow >nul 2>&1

echo [5/5] 检查本机信息...
echo.
echo  计算机名：%COMPUTERNAME%
echo  IP 地址：
ipconfig | findstr /i "IPv4"
echo.

echo ============================================
echo  配置完成。请重启这台计算机，
echo  然后在教师机的控制台点“刷新全部状态”。
echo ============================================
echo.
pause
