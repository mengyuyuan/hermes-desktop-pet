@echo off
echo === 编译星澜桌面宠物 ===
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:"%~dp0..\HongjunPet.exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll "%~dp0..\src\HongjunPet.cs"
if %ERRORLEVEL% EQU 0 (
    echo ✓ 编译成功！宠物程序已生成
) else (
    echo ✗ 编译失败，请检查错误信息
)
pause
