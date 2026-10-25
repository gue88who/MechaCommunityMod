// Inno Setup 6 runs as a 32-bit process, including in 64-bit install mode.
// Use native process enumeration without the WMI COM provider.
type
  TProcessEntry32W = record
    Size, Usage, ProcessID, DefaultHeapID, ModuleID, Threads, ParentProcessID: LongWord;
    Priority: LongInt;
    Flags: LongWord;
    ExeFile: array[0..259] of Char;
  end;

function ProcessSnapshot(Flags, ProcessID: LongWord): THandle;
  external 'CreateToolhelp32Snapshot@kernel32.dll stdcall';
function FirstProcess(Snapshot: THandle; var Entry: TProcessEntry32W): Boolean;
  external 'Process32FirstW@kernel32.dll stdcall';
function NextProcess(Snapshot: THandle; var Entry: TProcessEntry32W): Boolean;
  external 'Process32NextW@kernel32.dll stdcall';
function CloseProcessSnapshot(Snapshot: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function ProcessCheckFor(const ExeName: String): String;
var
  Snapshot: THandle;
  Entry: TProcessEntry32W;
  Name: String;
  I: Integer;
begin
  Result := 'Setup could not check running processes. Close Mechabellum and try again.';
  Entry.Size := SizeOf(Entry);
  if Entry.Size <> 556 then Exit; { PROCESSENTRY32W for the 32-bit Setup process }
  Snapshot := ProcessSnapshot(2, 0); { TH32CS_SNAPPROCESS }
  if Snapshot = THandle(-1) then Exit;
  try
    if not FirstProcess(Snapshot, Entry) then Exit;
    repeat
      Name := '';
      for I := 0 to 259 do begin
        if Entry.ExeFile[I] = #0 then Break;
        Name := Name + Entry.ExeFile[I];
      end;
      if CompareText(Name, ExeName) = 0 then begin
        Result := 'Close Mechabellum before installing or uninstalling Mecha Community Mod.';
        Exit;
      end;
    until not NextProcess(Snapshot, Entry);
    if DLLGetLastError = 18 then Result := ''; { ERROR_NO_MORE_FILES; other errors still block }
  finally
    CloseProcessSnapshot(Snapshot);
  end;
end;

function ProcessCheck: String;
begin
  Result := ProcessCheckFor('Mechabellum.exe');
end;
