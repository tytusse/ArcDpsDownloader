open System
open System.Net.Http
open System.Net
open System.Collections.Concurrent

let messageLoop = new BlockingCollection<unit->unit>()

let printfn msg = 
    let pr (str:string) = messageLoop.Add(fun () -> System.Console.WriteLine str)
    Printf.kprintf pr msg

let runUnsafe() =

    let url  = @"https://www.deltaconnected.com/arcdps/x64/d3d9.dll"
    let md5Url = @"https://www.deltaconnected.com/arcdps/x64/d3d9.dll.md5sum"
    let dst = @"D:\Games\Guild Wars 2\bin64\d3d9.dll"

    let http = new HttpClient()
    async {
        printfn "will download %s and %s" url md5Url
        let! data, md5 = async {
            match!
                [ http.GetAsync(url); http.GetAsync(md5Url) ]   
                |> List.map Async.AwaitTask
                |> Async.Parallel with
            | [|data; md5|] -> return data, md5
            | x -> return failwithf "expected two items, but got: %A" x }
    
        if data.StatusCode <> HttpStatusCode.OK
        then failwithf "failed downloading data"

        printfn "%s downloaded successfully" url

        if md5.StatusCode <> HttpStatusCode.OK
        then failwith "failed downloading md5"

        printfn "%s downloaded successfully" md5Url

        let! md5 = md5.Content.ReadAsStringAsync() |> Async.AwaitTask
        printfn "expected md5 is: %s" md5
        let md5 = 
            match md5.Split(" ") |> List.ofArray with
            | md5::_ -> md5.Trim().ToUpper()
            | _ -> failwithf "expected at least one part in md5 file"

        let! data = data.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
        let md5Handler = System.Security.Cryptography.MD5.Create()
        let actMd5 = 
            md5Handler.ComputeHash(data)
            |> Array.map (fun x -> x.ToString("X2"))
            |> String.concat ""

        printfn "actual md5 is: %s" actMd5

        if md5 <> actMd5 then failwith "actual md5 does not match expected one"

        printfn "writing result to %s" dst

        do! System.IO.File.WriteAllBytesAsync(dst, data) |> Async.AwaitTask

        printfn "done"
    } 
    |> Async.RunSynchronously

[<EntryPoint>]
let main _ =
    async{
        try
            try runUnsafe()
            with x ->
                printfn "program failed with:\n%APress any key to close" x
                messageLoop.Add (Console.ReadKey >> ignore)
        finally messageLoop.CompleteAdding()
    } |> Async.Start

    messageLoop.GetConsumingEnumerable() |> Seq.iter (fun x -> x())

    0
