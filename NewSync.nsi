!include "FileFunc.nsh"
!include "x64.nsh"

!define APPNAME "NewSync"
!define EXENAME "NewSync.exe"
!define INSTALLDIR "$PROGRAMFILES\${APPNAME}"
!define NSIS_OUTPUT_DIR "C:\\temp file transfer\\9.VisualStudio\\exe wrapper scripts\\NSIS Output"
!define SOLUTION_OUTPUT_DIR "C:\\temp file transfer\\9.VisualStudio\\field testing\\NewSync"

Outfile "${NSIS_OUTPUT_DIR}\\${APPNAME}_Install.exe"
!system 'cmd /C if not exist "${SOLUTION_OUTPUT_DIR}" mkdir "${SOLUTION_OUTPUT_DIR}"'
!system 'cmd /C copy /Y "${__FILE__}" "${SOLUTION_OUTPUT_DIR}\\${APPNAME}.nsi" >nul'
!finalize 'cmd /C copy /Y "%1" "${SOLUTION_OUTPUT_DIR}\\${APPNAME}_Install.exe" >nul'
InstallDir "${INSTALLDIR}"



RequestExecutionLevel admin

SilentInstall silent
SilentUninstall silent

Page instfiles

Function CloseAppIfRunning
    ; Force kill NewSync.exe using taskkill (runs as admin, most reliable)
    nsExec::ExecToLog 'taskkill /F /IM "${EXENAME}"'
    Sleep 1000
FunctionEnd

;--------------------------------
; Version Information
;--------------------------------
VIProductVersion "1.0.0.0"
VIAddVersionKey "CompanyName" "Austin Sharman"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2024 Austin Sharman. All rights reserved."
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "ProductVersion" "1.0.0.0"
VIAddVersionKey "Author" "Austin Sharman"
VIAddVersionKey "FileDescription" "App to launch and monitor any number of apps, designed to be ran as a custom shell app Written By Austin Sharman"
VIAddVersionKey "InternalName" "${APPNAME}"

;--------------------------------
; Installer Icon
;--------------------------------
Icon "C:\\temp file transfer\\9.VisualStudio\\field testing\\NewSync\\NewSync.app\\bin\\publish\\NewSync\\NewSync2.ico"

Section "Install"
    Call CloseAppIfRunning

    SetOutPath "$INSTDIR"

    ; Copy all files EXCEPT .pdb and .pdb-related files
    File /r /x "*.pdb" "C:\temp file transfer\9.VisualStudio\field testing\NewSync\NewSync.app\bin\publish\NewSync\*.*"

    ; Write uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; start menu shortcuts
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "/n"

    ; Write registry info
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${EXENAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "1.0.0.0"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1

  
SectionEnd


Section "Uninstall"

    ; Remove files
    Delete "$INSTDIR\*.*"
    RMDir /r "$INSTDIR"

    ; delete start menu shortcuts
    setshellvarcontext all
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"

    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"



SectionEnd
