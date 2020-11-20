namespace TeaDriven.Zokhion

module FileSystem =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Linq
    open System.Reactive.Linq
    open System.Reactive.Disposables

    open FSharp.Control.Reactive

    open Reactive.Bindings

    [<RequireQualifiedAccess>]
    module Directory =
        let inline exists path = Directory.Exists path

        let inline enumerateFiles directory =
            Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)

        let inline getDirectories directory = Directory.GetDirectories directory

        let inline getFiles directory =
            Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)

    [<RequireQualifiedAccess>]
    module File =
        let inline delete path = File.Delete path

        let inline exists path = File.Exists path

        let inline move sourceFileName destinationFileName =
            File.Move(sourceFileName, destinationFileName)

        let inline readAllLines path = File.ReadAllLines(path)

        let inline writeAllLines path (lines: #seq<string>) =
            File.WriteAllLines(path, lines)

    [<RequireQualifiedAccess>]
    module Path =
        let inline combine parts = Path.Combine parts

        let inline getDirectoryName path = Path.GetDirectoryName path

        let inline getExtension path = Path.GetExtension path

        let inline getFileName path = Path.GetFileName path

        let inline getFileNameWithoutExtension path =
            Path.GetFileNameWithoutExtension path

    type FileChange = Added | Removed

    type FileSystemCacheStatus =
        | Empty
        | Initializing of progress: float
        | Ready

    type FileSystemCache(directory: string) =
        let compositeDisposable = new CompositeDisposable()

        let watcher =
            new FileSystemWatcher(EnableRaisingEvents = false, IncludeSubdirectories = true)
        let fileSystemChanges =
            Observable.Create(fun observer ->
                [
                    watcher.Created |> Observable.map (fun e -> [ e.FullPath, Added ])
                    watcher.Deleted |> Observable.map (fun e -> [ e.FullPath, Removed ])
                    watcher.Renamed
                    |> Observable.map (fun e -> [ e.OldFullPath, Removed; e.FullPath, Added ])
                ]
                |> Observable.mergeSeq
                |> Observable.subscribeObserver observer)

        let mutable directories = Unchecked.defaultof<IDictionary<string, string list>>

        let mutable status = Unchecked.defaultof<ReactiveProperty<_>>

        do
            compositeDisposable.Add watcher

            status <- new ReactiveProperty<_>(Empty)

        interface IDisposable with
            member __.Dispose() =
                compositeDisposable.Dispose()

        member __.Status = status

        member __.DirectoriesWithFiles =
            directories
            |> Option.ofObj
            |> Option.map (fun directories -> directories |> Seq.toList)
            |> Option.defaultValue []

        member __.Initialize(baseDirectory: string) =
            status.Value <- Initializing 0.
            watcher.EnableRaisingEvents <- false

            async {
                let subDirectories = Directory.GetDirectories baseDirectory

                let data =
                    subDirectories
                    |> Array.mapi (fun index directory ->
                        let files = Directory.GetFiles directory

                        status.Value <- Initializing (float index/float subDirectories.Length)

                        directory, files |> Array.toList)

                directories <-
                    data.ToDictionary(
                        (fun (directory, _) -> directory),
                        (fun (_, files) -> files))

                status.Value <- Ready

                watcher.Path <- baseDirectory
                watcher.EnableRaisingEvents <- true
            }
