open ArcDpsDownloader.Lib

[<EntryPoint>]
let main argv =
    Registry.readStringValue Registry.pathRegistryValue
    |> function
    | Some path -> printfn $"Guild Wars 2 path from registry: {path}"
    | None -> printfn"Guild Wars 2 path not found in registry."

    0
