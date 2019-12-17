open System
open System.Net.Http
open System.Net
open System.IO
open System.Collections.Concurrent
open FSharpx.Control

let messageLoop = new BlockingCollection<unit->unit>()

let printfn msg = 
    let pr (str:string) = messageLoop.Add(fun () -> System.Console.WriteLine str)
    Printf.kprintf pr msg

let http = new HttpClient()
let download url = async {
    printfn "will download %s" url
    let! r = http.GetAsync(url) |> Async.AwaitTask
    if r.StatusCode <> HttpStatusCode.OK
    then failwithf "failed downloading %s with [%O] %s" url r.StatusCode r.ReasonPhrase

    printfn "%s downloaded successfully" url 
    return r
}

let downloadString url =
    download url
    |> Async.bind(fun x -> x.Content.ReadAsStringAsync() |> Async.AwaitTask)

let downloadBytes url =
    download url
    |> Async.bind(fun x -> x.Content.ReadAsByteArrayAsync() |> Async.AwaitTask)

let inline split (delim:string) (str:string) = str.Split(delim)
let runUnsafe() =

    let binUrl  = @"https://www.deltaconnected.com/arcdps/x64/d3d9.dll"
    let md5Url = @"https://www.deltaconnected.com/arcdps/x64/d3d9.dll.md5sum"
    let binLocalPath = @"D:\Games\Guild Wars 2\bin64\d3d9.dll"
    let lastModifiedLocalPath = @"D:\Games\Guild Wars 2\bin64\d3d9.dll.lastModified"
    
    async {
        let! lastModDisk = async {
            if File.Exists lastModifiedLocalPath
            then return! (File.ReadAllTextAsync lastModifiedLocalPath |> Async.AwaitTask |> Async.map Some)
            else return None
        }

        let! md5Result = download md5Url

        let lastModServer = 
            md5Result.Content.Headers.LastModified 
            |> Option.ofNullable
            |> Option.map(fun d -> d.ToString("o"))
        
        match lastModDisk, lastModServer with
        | Some lastModDisk, Some lastModServer when lastModDisk = lastModServer ->
            printfn "Server modification date is the same as disk date (%s) - not downloading" lastModDisk
        | _ ->
            printfn "modification dates: local = %A, server = %A" lastModDisk lastModServer
            let! md5 = md5Result.Content.ReadAsStringAsync() |> Async.AwaitTask
            printfn "expected md5 is: %s" md5

            let md5 = 
                match md5.Split(" ") |> List.ofArray with
                | md5::_ -> md5.Trim().ToUpper()
                | _ -> failwithf "expected at least one part in md5 file"

            let! bin = downloadBytes binUrl
            let md5Handler = System.Security.Cryptography.MD5.Create()
            let actMd5 = 
                md5Handler.ComputeHash(bin)
                |> Array.map (fun x -> x.ToString("X2"))
                |> String.concat ""

            printfn "actual md5 is: %s" actMd5

            if md5 <> actMd5 then failwith "actual md5 does not match expected one"

            printfn "writing bin to %s" binLocalPath
            
            // sync write bin then md5
            do! File.WriteAllBytesAsync(binLocalPath, bin) |> Async.AwaitTask
            match lastModServer with
            | Some data ->
                printfn "writing last mod date to %s" lastModifiedLocalPath
                do! File.WriteAllTextAsync(lastModifiedLocalPath, data) |> Async.AwaitTask
            | None -> 
                printfn "NOTE: server last modification date not known - not writing it locally"

        printfn "done"
    } 
    |> Async.RunSynchronously

[<EntryPoint>]
let main _ =
    async{
        try
            try 
                runUnsafe()
                do! Async.Sleep 3000
            with x ->
                printfn "program failed with:\n%APress any key to close" x
                messageLoop.Add (Console.ReadKey >> ignore)
        finally messageLoop.CompleteAdding()
    } |> Async.Start

    messageLoop.GetConsumingEnumerable() |> Seq.iter (fun x -> x())

    0
