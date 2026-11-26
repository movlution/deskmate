; Deskmate per-user installer (Inno Setup 6)
; Installs to %LOCALAPPDATA%\Programs\Deskmate: no admin prompt, and the in-app updater
; can still swap Deskmate.exe in place. Build with build-installer.cmd.

#ifndef SourceExe
  #define SourceExe AddBackslash(SourcePath) + "..\Deskmate.exe"
#endif
#define AppVersion GetVersionNumbersString(SourceExe)
; Test builds pass their own /DAppGuid so they never replace the real install's Apps entry
#ifndef AppGuid
  #define AppGuid "4AAF7005-4904-4C42-83E7-288DF10790FE"
#endif

[Setup]
AppId={{{#AppGuid}}
AppName=Deskmate
AppVersion={#AppVersion}
AppVerName=Deskmate {#AppVersion}
AppPublisher=movlution
AppPublisherURL=https://github.com/movlution/deskmate
AppSupportURL=https://github.com/movlution/deskmate/issues
AppUpdatesURL=https://github.com/movlution/deskmate/releases
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Deskmate
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\release
OutputBaseFilename=Deskmate-Setup
SetupIconFile=..\src\Deskmate.ico
UninstallDisplayIcon={app}\Deskmate.exe
UninstallDisplayName=Deskmate
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Deskmate closes itself in PrepareToInstall; Restart Manager handles agent relays and MCP copies
CloseApplications=yes
RestartApplications=no

[Languages]
; Inno Setup ships no Indonesian file: English base, with every screen this wizard shows overridden below
Name: "id"; MessagesFile: "compiler:Default.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
id.LanguageName=Bahasa Indonesia
id.SetupAppTitle=Pemasangan
id.SetupWindowTitle=Pemasangan - %1
id.UninstallAppTitle=Copot
id.UninstallAppFullTitle=Copot %1
id.ConfirmTitle=Konfirmasi
id.ErrorTitle=Galat
id.ExitSetupTitle=Keluar dari Pemasangan
id.ExitSetupMessage=Pemasangan belum selesai. Kalau keluar sekarang, program tidak akan terpasang.%n%nAnda bisa menjalankan pemasangan lagi kapan saja.%n%nKeluar dari pemasangan?
id.ButtonBack=< &Kembali
id.ButtonNext=&Lanjut >
id.ButtonInstall=&Pasang
id.ButtonOK=OK
id.ButtonCancel=Batal
id.ButtonYes=&Ya
id.ButtonNo=&Tidak
id.ButtonFinish=&Selesai
id.ButtonWizardBrowse=&Telusuri...
id.SelectLanguageTitle=Pilih Bahasa Pemasangan
id.SelectLanguageLabel=Pilih bahasa yang dipakai selama pemasangan.
id.WizardSelectTasks=Pilih Tugas Tambahan
id.SelectTasksDesc=Tugas tambahan apa yang ingin dijalankan?
id.SelectTasksLabel2=Pilih tugas tambahan saat memasang [name], lalu klik Lanjut.
id.AdditionalIcons=Pintasan tambahan:
id.WizardPreparing=Bersiap Memasang
id.PreparingDesc=Pemasangan sedang bersiap memasang [name] di komputer Anda.
id.CannotContinue=Pemasangan tidak bisa dilanjutkan. Klik Batal untuk keluar.
id.ApplicationsFound=Aplikasi berikut memakai file yang perlu diperbarui. Sebaiknya biarkan pemasangan menutupnya otomatis.
id.ApplicationsFound2=Aplikasi berikut memakai file yang perlu diperbarui. Sebaiknya biarkan pemasangan menutupnya otomatis. Setelah selesai, pemasangan akan mencoba membukanya lagi.
id.CloseApplications=&Tutup aplikasi secara otomatis
id.DontCloseApplications=&Jangan tutup aplikasi
id.WizardInstalling=Memasang
id.InstallingLabel=Tunggu sebentar, [name] sedang dipasang di komputer Anda.
id.FinishedHeadingLabel=Pemasangan [name] Selesai
id.FinishedLabelNoIcons=[name] sudah terpasang di komputer Anda.
id.FinishedLabel=[name] sudah terpasang di komputer Anda. Buka lewat pintasan yang telah dibuat.
id.ClickFinish=Klik Selesai untuk keluar.
id.SetupAborted=Pemasangan tidak selesai.%n%nPerbaiki masalahnya lalu jalankan pemasangan lagi.
id.StatusClosingApplications=Menutup aplikasi...
id.StatusExtractFiles=Menyalin file...
id.StatusCreateIcons=Membuat pintasan...
id.StatusRunProgram=Menyelesaikan pemasangan...
id.StatusRollback=Membatalkan perubahan...
id.ConfirmUninstall=Yakin ingin mencopot %1 sepenuhnya?
id.UninstallStatusLabel=Tunggu sebentar, %1 sedang dicopot dari komputer Anda.
id.UninstalledAll=%1 berhasil dicopot dari komputer Anda.
id.UninstalledMost=%1 selesai dicopot.%n%nAda bagian yang tidak bisa dihapus; hapus secara manual.
id.WizardUninstalling=Status Pencopotan
id.StatusUninstalling=Mencopot %1...

[CustomMessages]
id.DesktopIcon=Buat ikon di desktop
en.DesktopIcon=Create a desktop icon
id.Launch=Jalankan Deskmate
en.Launch=Launch Deskmate
id.Adopting=Menghubungkan autostart dan agent ke salinan ini...
en.Adopting=Pointing autostart and agents at this copy...
id.StillRunning=Deskmate belum bisa ditutup. Tutup dari menu island (Keluar), lalu coba lagi.
en.StillRunning=Deskmate could not be closed. Quit it from the island menu, then try again.
id.UninstallRunning=Deskmate belum tertutup, jadi hook agent dan autostart mungkin masih tertinggal. Tutup Deskmate lalu jalankan uninstall lagi kalau perlu.
en.UninstallRunning=Deskmate did not close, so agent hooks and autostart may remain. Quit Deskmate and run the uninstaller again if needed.
id.PurgeData=Hapus juga data Deskmate (pengaturan, catatan, Ingatan, pet) di:%n%1%n%nData yang dihapus tidak bisa dikembalikan.
en.PurgeData=Also delete Deskmate's data (settings, notes, Ingatan, pets) in:%n%1%n%nDeleted data cannot be recovered.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[InstallDelete]
; Updates overwrite in place: no backups of replaced builds are kept
Type: files; Name: "{app}\Deskmate.new.exe"
Type: files; Name: "{app}\Deskmate.old.exe"
Type: files; Name: "{app}\Deskmate.previous-*.exe"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Deskmate.exe"; Flags: ignoreversion

[Icons]
; Same name the island keeps itself, so it does not make a second one
Name: "{userprograms}\Deskmate"; Filename: "{app}\Deskmate.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Deskmate"; Filename: "{app}\Deskmate.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Deskmate.exe"; Parameters: "--adopt"; StatusMsg: "{cm:Adopting}"; Flags: runhidden waituntilterminated
Filename: "{app}\Deskmate.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[Registry]
; The app's first-run setup starts in the language picked here
Root: HKCU; Subkey: "Software\Deskmate"; ValueType: string; ValueName: "SetupLang"; ValueData: "{language}"; Flags: uninsdeletekey

[UninstallDelete]
; Updater leftovers and backups of replaced builds beside the exe
Type: files; Name: "{app}\Deskmate.new.exe"
Type: files; Name: "{app}\Deskmate.old.exe"
Type: files; Name: "{app}\Deskmate.previous-*.exe"
Type: dirifempty; Name: "{app}"

[Code]
const
  StillRunning = 2;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  { The new copy closes whatever version is running, through the island's own Quit path }
  ExtractTemporaryFile('Deskmate.exe');
  if Exec(ExpandConstant('{tmp}\Deskmate.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = StillRunning) then
    Result := CustomMessage('StillRunning');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if Exec(ExpandConstant('{app}\Deskmate.exe'), '--uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = StillRunning) then
      if not UninstallSilent then
        MsgBox(CustomMessage('UninstallRunning'), mbError, MB_OK);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Deskmate');
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox(FmtMessage(CustomMessage('PurgeData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
