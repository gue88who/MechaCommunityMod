// Uses public GitHub releases; downloaded installers require a matching checksum.
const
  ReleaseRoot = 'https://github.com/gue88who/MechaCommunityMod/releases/';

var
  UpdateButton: TNewButton;
  UpdateReleaseURL: String;
  UpdateDownloadPage: TDownloadWizardPage;
  UpdateHandedOff: Boolean;

function InstallerName(Tag: String): String;
begin
  if Copy(Tag, 1, 1) = 'v' then Delete(Tag, 1, 1);
  Result := 'MechaCommunityMod-' + Tag + '-Setup.exe';
end;

function InstallerChecksum(Text, FileName: String): String;
var I: Integer;
begin
  Result := '';
  Text := Trim(Text);
  if Length(Text) <> 66 + Length(FileName) then Exit;
  if Copy(Text, 65, Length(Text)) <> '  ' + FileName then Exit;
  for I := 1 to 64 do
    if Pos(Lowercase(Text[I]), '0123456789abcdef') = 0 then Exit;
  Result := Lowercase(Copy(Text, 1, 64));
end;

function NewerReleaseURL(Status: Integer; Location, CurrentVersion: String): String;
var
  Tag, VersionText: String;
  Current, Latest: Int64;
  I, Parts: Integer;
begin
  Result := '';
  if (Status <> 302) and (Status <> 301) then Exit;
  if Pos(ReleaseRoot + 'tag/', Location) <> 1 then Exit;
  Tag := Copy(Location, Length(ReleaseRoot + 'tag/') + 1, Length(Location));
  VersionText := Tag;
  if Copy(VersionText, 1, 1) = 'v' then Delete(VersionText, 1, 1);
  if (Length(VersionText) < 5) or (Length(VersionText) > 32) then Exit;
  Parts := 1;
  for I := 1 to Length(VersionText) do begin
    if VersionText[I] = '.' then begin
      if (I = 1) or (I = Length(VersionText)) then Exit;
      if VersionText[I - 1] = '.' then Exit;
      Parts := Parts + 1;
    end else if (VersionText[I] < '0') or (VersionText[I] > '9') then Exit;
  end;
  if (Parts < 3) or (Parts > 4) then Exit;
  if not StrToVersion(CurrentVersion, Current) then Exit;
  if not StrToVersion(VersionText, Latest) then Exit;
  if ComparePackedVersion(Latest, Current) > 0 then Result := Location;
end;

procedure CheckUpdateRules;
begin
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v0.9.101', '0.9.100') = '' then
    RaiseException('Newer release was not detected.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/1.0.0', '0.9.100') = '' then
    RaiseException('Major release was not detected.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v0.9.100', '0.9.100') <> '' then
    RaiseException('Equal version offered an update.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v0.9.99', '0.9.100') <> '' then
    RaiseException('Older version offered an update.');
  if NewerReleaseURL(404, ReleaseRoot + 'tag/v1.0.0', '0.9.100') <> '' then
    RaiseException('Missing release offered an update.');
  if NewerReleaseURL(302, 'https://example.com/tag/v1.0.0', '0.9.100') <> '' then
    RaiseException('Foreign release URL was accepted.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v1.0.0-beta', '0.9.100') <> '' then
    RaiseException('Prerelease version was accepted.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v1.0.0/extra', '0.9.100') <> '' then
    RaiseException('Malformed tag was accepted.');
  if NewerReleaseURL(302, ReleaseRoot + 'tag/v1..0', '0.9.100') <> '' then
    RaiseException('Malformed version was accepted.');
  if InstallerName('v1.0.0') <> 'MechaCommunityMod-1.0.0-Setup.exe' then
    RaiseException('Installer asset name was incorrect.');
  if InstallerChecksum(StringOfChar('a', 64) + '  setup.exe', 'setup.exe') = '' then
    RaiseException('Valid checksum was rejected.');
  if InstallerChecksum(StringOfChar('a', 64) + '  other.exe', 'setup.exe') <> '' then
    RaiseException('Checksum for another file was accepted.');
  if InstallerChecksum(StringOfChar('z', 64) + '  setup.exe', 'setup.exe') <> '' then
    RaiseException('Invalid checksum was accepted.');
  if InstallerChecksum('', 'setup.exe') <> '' then
    RaiseException('Missing checksum was accepted.');
  Log('PASS update version, missing-release and URL validation checks');
end;

procedure UpdateClicked(Sender: TObject);
var
  Code: Integer;
  Tag, FileName, DownloadRoot, Hash: String;
  ChecksumText: AnsiString;
begin
  if UpdateReleaseURL = '' then Exit;
  Tag := Copy(UpdateReleaseURL, Length(ReleaseRoot + 'tag/') + 1, Length(UpdateReleaseURL));
  FileName := InstallerName(Tag);
  DownloadRoot := ReleaseRoot + 'download/' + Tag + '/';
  UpdateButton.Enabled := False;
  UpdateDownloadPage.Clear;
  UpdateDownloadPage.Add(DownloadRoot + FileName + '.sha256', FileName + '.sha256', '');
  UpdateDownloadPage.Show;
  try
    try
      UpdateDownloadPage.Download;
      if not LoadStringFromFile(ExpandConstant('{tmp}\') + FileName + '.sha256', ChecksumText) then
        RaiseException('Could not read the installer checksum.');
      Hash := InstallerChecksum(String(ChecksumText), FileName);
      if Hash = '' then RaiseException('The release has no valid checksum for this installer.');
      UpdateDownloadPage.Clear;
      UpdateDownloadPage.Add(DownloadRoot + FileName, FileName, Hash);
      UpdateDownloadPage.Download; { Inno verifies SHA-256 before returning. }
      UpdateDownloadPage.Hide;
      WizardForm.Hide;
      { Keep the temporary directory alive until the new Setup has finished. }
      if not ShellExecAsOriginalUser('open', ExpandConstant('{tmp}\') + FileName,
        '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
        RaiseException('Could not start the downloaded installer.');
      if Code <> 0 then RaiseException('The newer installer did not complete. You can retry or install this version.');
      UpdateHandedOff := True;
    except
      WizardForm.Show;
      if not UpdateDownloadPage.AbortedByUser then
        MsgBox(GetExceptionMessage, mbError, MB_OK);
    end;
  finally
    UpdateDownloadPage.Hide;
    UpdateButton.Enabled := True;
  end;
  if UpdateHandedOff then WizardForm.Close;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if UpdateHandedOff then begin Cancel := True; Confirm := False; end;
end;

procedure InitializeUpdateCheck;
var
  Request: Variant;
begin
  if WizardSilent then Exit;
  UpdateDownloadPage := CreateDownloadPage('Downloading update', 'Downloading and verifying the newer installer.', nil);
  UpdateDownloadPage.ShowBaseNameInsteadOfUrl := True;
  UpdateButton := TNewButton.Create(WizardForm);
  UpdateButton.Parent := WizardForm;
  UpdateButton.Left := ScaleX(16);
  UpdateButton.Top := WizardForm.NextButton.Top;
  UpdateButton.Width := ScaleX(110);
  UpdateButton.Height := WizardForm.NextButton.Height;
  UpdateButton.Caption := 'Update available';
  UpdateButton.Visible := False;
  UpdateButton.OnClick := @UpdateClicked;
  try
    Request := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Request.SetTimeouts(1500, 1500, 1500, 1500);
    Request.Open('HEAD', ReleaseRoot + 'latest', True);
    Request.Option[6] := False; { Read only GitHub's redirect, never follow it. }
    Request.SetRequestHeader('User-Agent', 'MechaCommunityMod-Setup/{#ModVersion}');
    Request.Send();
    if not Request.WaitForResponse(5) then begin
      Request.Abort();
      Log('Update check timed out; continuing with bundled installer.');
      Exit;
    end;
    if (Request.Status = 301) or (Request.Status = 302) then
      UpdateReleaseURL := NewerReleaseURL(Request.Status,
        Request.GetResponseHeader('Location'), '{#ModVersion}');
    UpdateButton.Visible := UpdateReleaseURL <> '';
  except
    Log('Update check unavailable; continuing with bundled installer.');
  end;
end;
