@echo off
chcp 65001 >nul
title 编译鸿钧宠物
cls
echo ================================
echo    鸿钧桌面宠物 - 编译工具
echo ================================
echo.
echo 正在编译...
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:winexe /out:D:\鸿钧浮窗\HongjunPet.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll D:\鸿钧浮窗\HongjunPet.cs
echo.
if %errorlevel% equ 0 (
    echo ✅ 编译成功！
    echo 文件: D:\鸿钧浮窗\HongjunPet.exe
    echo.
    echo 按任意键启动宠物...
    pause >nul
    start /min "" "D:\鸿钧浮窗\HongjunPet.exe"
    echo 已启动！
) else (
    echo ❌ 编译失败，错误码: %errorlevel%
    echo 请检查 D:\鸿钧浮窗\HongjunPet.cs 的语法
    pause
)
