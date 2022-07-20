open System
open System.Net.Http
open System.Net
open System.IO
open System.Collections.Concurrent
open FSharpx.Control

let startedAt = DateTime.Now
let gameDirPath = @"D:\Games\Guild Wars 2\"
let binUrl  = @"https://www.deltaconnected.com/arcdps/x64/d3d11.dll"
let md5Url = @"https://www.deltaconnected.com/arcdps/x64/d3d11.dll.md5sum"
let binLocalPaths =
    [
        @"bin64\d3d9.dll"
        @"d3d11.dll"
    ]
    |> List.map (fun x -> gameDirPath + x)
    
let lastModifiedLocalPath = gameDirPath + @"arcdps.lastModified"
let logPath = gameDirPath + @"arcdps.download.log"

let messageLoop = new BlockingCollection<unit->unit>()

let printToQueue msg = 
    let pr (str:string) = messageLoop.Add(fun () -> 
        let now = DateTime.Now
        let sinceStart = now - startedAt
        let msg = 
            sprintf "%s[%fms] %s"
                (now.ToString("HH:mm:ss.fff"))
                sinceStart.TotalMilliseconds
                str
        Printf.printfn $"%s{msg}"
        try 
            File.AppendAllText(logPath, msg+"\n")
        with x ->
            Printf.printfn $"failed adding to log fue to: {x}"
        )
    Printf.kprintf pr msg

let http = new HttpClient()
let headers url = async {
    printToQueue $"HEAD %s{url}"
    use msg = new HttpRequestMessage(HttpMethod.Head, url)
    let! h = http.SendAsync msg |> Async.AwaitTask
    return h
}
    
let download url = async {
    printToQueue $"GET %s{url}"
    let! r = http.GetAsync(url) |> Async.AwaitTask
    if r.StatusCode <> HttpStatusCode.OK
    then failwithf $"failed downloading %s{url} with [{r.StatusCode}] %s{r.ReasonPhrase}"

    printToQueue $"done: GET %s{url}" 
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
    printToQueue "started at: %s" (startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"))
    
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
            printToQueue $"Server modification date is the same as disk date (%s{lastModDisk}) - not downloading"
        | _ ->
            printToQueue $"modification dates: local = %A{lastModDisk}, server = %A{lastModServer}"
            let! md5 = md5Result.Content.ReadAsStringAsync() |> Async.AwaitTask
            printToQueue $"expected md5 is: %s{md5}"

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

            printToQueue $"actual md5 is: %s{actMd5}"

            if md5 <> actMd5 then failwith "actual md5 does not match expected one"

            printToQueue $"writing bin to %A{binLocalPaths}"
            
            // sync write bin then md5
            for binLocalPath in binLocalPaths do
                do! File.WriteAllBytesAsync(binLocalPath, bin) |> Async.AwaitTask
            match lastModServer with
            | Some data ->
                printToQueue $"writing last mod date to %s{lastModifiedLocalPath}"
                do! File.WriteAllTextAsync(lastModifiedLocalPath, data) |> Async.AwaitTask
            | None -> 
                printToQueue "NOTE: server last modification date not known - not writing it locally"

        printToQueue "done"
    } 

[<EntryPoint>]
let main _ =
    async{
        try
            try 
                do! runUnsafe()
                do! Async.Sleep 3000
            with x ->
                printToQueue $"program failed with:\n%A{x}Press any key to close"
                messageLoop.Add (Console.ReadKey >> ignore)
        finally messageLoop.CompleteAdding()
    } |> Async.Start

    messageLoop.GetConsumingEnumerable() |> Seq.iter (fun x -> x())

    0
