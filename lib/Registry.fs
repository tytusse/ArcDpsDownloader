module ArcDpsDownloader.Lib.Registry

open Microsoft.Win32
open System

/// Reads a string value from the Windows registry.
/// 
/// Parameters:
///   - keyPath: Full path to the registry key (e.g., "HKEY_LOCAL_MACHINE\Software\...")
/// 
/// Returns:
///   - Some value if the value exists and is a string
///   - None if the value does not exist
/// 
/// Raises:
///   - InvalidOperationException if the value exists but is not of string type
let readStringValue (keyPath: string) : string option =
    let parts = keyPath.Split('\\')
    if parts.Length < 2 then
        invalidArg (nameof keyPath) "Registry key path must include at least a hive and a key name"
    
    let hiveString = parts.[0]
    let subKeyPath = String.concat "\\" parts.[1..]
    
    let hive = 
        match hiveString with
        | "HKEY_LOCAL_MACHINE" | "HKLM" -> RegistryHive.LocalMachine
        | "HKEY_CURRENT_USER" | "HKCU" -> RegistryHive.CurrentUser
        | "HKEY_CLASSES_ROOT" | "HKCR" -> RegistryHive.ClassesRoot
        | "HKEY_USERS" | "HKU" -> RegistryHive.Users
        | "HKEY_CURRENT_CONFIG" -> RegistryHive.CurrentConfig
        | _ -> invalidArg (nameof keyPath) $"Unknown registry hive: {hiveString}"
    
    let lastBackslash = subKeyPath.LastIndexOf('\\')
    let (keyOnlyPath, valueName) = 
        if lastBackslash = -1 then
            ("", subKeyPath)
        else
            (subKeyPath.[..lastBackslash-1], subKeyPath.[lastBackslash+1..])
    
    try
        use key = RegistryKey.OpenBaseKey(hive, RegistryView.Default)
        use subKey = key.OpenSubKey(keyOnlyPath, false)
        
        match subKey with
        | null -> None
        | _ ->
            match subKey.GetValue(valueName) with
            | null -> None
            | value ->
                match value with
                | :? string as str -> Some str
                | _ -> 
                    let valueType = value.GetType().Name
                    raise (InvalidOperationException $"Registry value '{valueName}' is of type {valueType}, not string")
    with
    | :? UnauthorizedAccessException -> None
    | :? InvalidOperationException -> reraise()
let pathRegistryValue = @"HKEY_LOCAL_MACHINE\SOFTWARE\ArenaNet\Guild Wars 2\Path"
let gameDirPath() = 
    let pathFromReg = readStringValue pathRegistryValue
    match pathFromReg with
    | Some p -> System.IO.Path.GetDirectoryName p
    | None -> @"C:\Program Files\Guild Wars 2"