' Launcher invisibile tramite host ufficiale Microsoft .NET (zero finestre console, zero falsi positivi Antivirus)
Option Explicit
Dim fso, shell, baseDir, dllPath, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
dllPath = fso.BuildPath(baseDir, "PixelDesktopWidget.dll")
shell.CurrentDirectory = baseDir
cmd = """C:\Program Files\dotnet\dotnet.exe"" """ & dllPath & """"
shell.Run cmd, 0, False
