namespace TeaDriven.Zokhion.ViewModels

open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Diagnostics
open System.IO
open System.Linq
open System.Reactive.Concurrency
open System.Reactive.Linq
open System.Reactive.Subjects
open System.Text.RegularExpressions
open System.Windows
open System.Windows.Input

open Dragablz

open DynamicData

open FSharp.Control.Reactive

open Reactive.Bindings
open Reactive.Bindings.Notifiers

open ReactiveUI

open TeaDriven.Zokhion
open TeaDriven.Zokhion.FileSystem

type GroupCategoryEntry =
    {
        Name: string
        GroupCategory: GroupCategory
    }

type RenamedFile = { OriginalFile: FileInfo; NewFilePath: string }

type FileChanges =
    {
        RenamedFiles: IDictionary<string, RenamedFile>
        DeletedFiles: IDictionary<string, FileInfo>
    }

type FileOperation =
    | AddRename of oldFile:FileInfo * newName:string
    | RemoveRename of oldNames:string list
    | AddDelete of FileInfo
    | RemoveDelete of string list

type UnderscoreHandling = Ignore = 0 | Replace = 1 | TrimSuffix = 2

type LogLevel =
    | Diagnostic
    | Informational
    | Warning
    | Critical

type Activity =
    | KeepAlive
    | RenameSuccess
    | RenameFailure
    | DeleteSuccess
    | DeleteFailure
    | AddName

type LogEntry =
    {
        Timestamp: DateTimeOffset
        LogLevel: LogLevel
        Activity: Activity
        Message: string
    }

type FileSystemWatcherStatus =
    | NewlyAlive
    | AlreadyAlive
    | NewlyDead
    | AlreadyDead

[<AllowNullLiteral>]
type DirectoryViewModel(directoryInfo: DirectoryInfo, fileCount: int) =
    member __.DirectoryInfo = directoryInfo
    member __.FullName = directoryInfo.FullName
    member __.Name = directoryInfo.Name
    member __.FileCount = fileCount

type MainWindowViewModel() as this =
    inherit ReactiveObject()

    let hasTouchInput =
        Tablet.TabletDevices
        |> Seq.cast<TabletDevice>
        |> Seq.exists (fun tablet -> tablet.Type = TabletDeviceType.Touch)

    let baseDirectory = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let filterBySourceDirectoryPrefixes = new ReactiveProperty<_>(true)
    let sourceDirectoryPrefixes =
        new ReactiveProperty<_>("", ReactivePropertyMode.RaiseLatestValueOnSubscribe)
    let selectedDirectory =
        new ReactiveProperty<_>(Unchecked.defaultof<DirectoryViewModel>, ReactivePropertyMode.None)
    let directories = ObservableCollection()
    let mutable refreshDirectoriesCommand = Unchecked.defaultof<ReactiveCommand>

    let mutable isBaseDirectoryValid = Unchecked.defaultof<ReadOnlyReactiveProperty<bool>>

    let searchCommands =
        new SelectiveBehaviorSubject<_>(function | Directories _ -> true | _ -> false)
        :> ISubject<SearchViewModelCommand>
    let searches = ObservableCollection<SearchViewModel>()
    let activeSearchTab = new ReactiveProperty<SearchViewModel>()
    let selectedFilesSubject = new System.Reactive.Subjects.Subject<IObservable<FileInfo>>()

    let mutable selectedFile = Unchecked.defaultof<ReadOnlyReactiveProperty<FileInfo>>

    let mutable saveSettingsCommand = Unchecked.defaultof<ReactiveCommand>

    let originalFileName = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let originalFileNameSelectedText = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let newFileName = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let newFileNameSelectedText = new ReactiveProperty<_>("", ReactivePropertyMode.None)

    let treatParenthesizedPartAsNames = new ReactiveProperty<_>(true)
    let fixupNamesInMainPart = new ReactiveProperty<_>(true)
    let underscoreHandling = new ReactiveProperty<_>(UnderscoreHandling.Ignore)
    let detectNamesInMainAndNamesParts = new ReactiveProperty<_>(false)
    let recapitalizeNames = new ReactiveProperty<_>(false)

    let mutable openCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable openFromSearchCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable openExplorerCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable showFilePropertiesCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable deleteFileCommand = Unchecked.defaultof<ReactiveCommand<_, _>>

    let mutable fileChanges = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    let selectedDestinationDirectory =
        new ReactiveProperty<_>(Unchecked.defaultof<DirectoryInfo>, ReactivePropertyMode.None)
    let destinationDirectoryPrefixes = new ReactiveProperty<_>("")
    let destinationDirectories = ObservableCollection()

    let toReplaceToAdd = new ReactiveProperty<_>("")
    let replaceWithToAdd = new ReactiveProperty<_>("")
    let mutable addReplacementCommand = Unchecked.defaultof<ReactiveCommand>
    let replacements = ObservableCollection()

    let newNameToAdd = new ReactiveProperty<_>("", ReactivePropertyMode.RaiseLatestValueOnSubscribe)
    let mutable clearNewNameToAddCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable addNameCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable setNameFilterCommand = Unchecked.defaultof<ReactiveCommand>
    let allNames = new SourceCache<NameViewModel, string>(fun vm -> vm.Name.Value)

    let mutable selectedNames = Unchecked.defaultof<ReadOnlyObservableCollection<_>>
    let mutable names = Unchecked.defaultof<ReadOnlyObservableCollection<_>>

    let mutable resetNameSelectionCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable searchForTextCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable searchForNameCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable deleteNameCommand = Unchecked.defaultof<ReactiveCommand>

    let editingFeatureInstances = ObservableCollection()
    let editingFeatureName = new ReactiveProperty<_>()
    let editingFeatureCode = new ReactiveProperty<_>()
    let editingFeatureToInclude = new ReactiveProperty<_>()
    let mutable confirmEditingFeatureCommand = Unchecked.defaultof<ReactiveCommand>
    let selectedFeature =
        new ReactiveProperty<_>(Unchecked.defaultof<FeatureViewModel>, ReactivePropertyMode.None)
    let features = ReactiveList()
    let featureInstances = ReactiveList(ChangeTrackingEnabled = true)
    let mutable groupCategories = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    let enableFeatureEditing = new ReactiveProperty<_>()
    let mutable removeFeatureInstanceRowCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable addFeatureInstanceRowCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable clearSelectedFeatureCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable expandAllFeaturesCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable collapseAllFeaturesCommand = Unchecked.defaultof<ReactiveCommand>

    let resultingFilePath = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let mutable applyCommand = Unchecked.defaultof<ReactiveCommand<_, _>>

    let watcher = new FileSystemWatcher(EnableRaisingEvents = false, IncludeSubdirectories = true)
    let mutable reviveFileSystemWatcherCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable isFileSystemWatcherAlive = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    let mutable activityLog = ObservableCollection()
    let activityLogSubject = new System.Reactive.Subjects.Subject<_>()
    let log logLevel activity message =
        {
            Timestamp = DateTimeOffset.Now
            LogLevel = logLevel
            Activity = activity
            Message = message
        }
        |> activityLogSubject.OnNext

    let updateDirectoriesList baseDirectory prefixes filterByPrefixes =
        directories.Clear()

        Directory.getDirectories baseDirectory
        |> Seq.map DirectoryInfo
        |> Seq.filter (fun di ->
            match filterByPrefixes, prefixes with
            | false, _ | _, "" -> true
            | _ -> prefixes |> Seq.exists (string >> di.Name.StartsWith))
        |> Seq.map (fun di ->
            let files = di.GetFiles()

            DirectoryViewModel(di, files.Length))
        |> Seq.sortWith (fun x y -> Interop.StrCmpLogicalW(x.Name, y.Name))
        |> Seq.iter directories.Add

    let updateDestinationDirectories (prefixes: string) (currentFilePath: string) =
        let startsWithAny parts (s: string) =
            parts |> Seq.toList |> List.exists (string >> s.StartsWith)

        let currentFileDirectory = Path.getDirectoryName currentFilePath

        destinationDirectories.Clear()

        let filter =
            if String.IsNullOrWhiteSpace prefixes
            then fun _ -> true
            else startsWithAny prefixes

        [
            currentFileDirectory

            yield!
                Directory.getDirectories this.BaseDirectory.Value
                |> Array.filter (Path.getFileName >> filter)
                |> Array.sortWith (fun x y -> Interop.StrCmpLogicalW(x, y))
                |> Array.toList
        ]
        |> List.distinct
        |> List.map DirectoryInfo
        |> List.iter destinationDirectories.Add

        this.SelectedDestinationDirectory.Value <-
            destinationDirectories
            |> Seq.find (fun (d: DirectoryInfo) -> d.FullName = currentFileDirectory)

    let clearNewNameToAdd () = this.NewNameToAdd.Value <- ""

    let addName name =
        if not <| String.IsNullOrWhiteSpace name
        then
            let name = trim name

            let viewModel =
                allNames.Items
                |> Seq.tryFind (fun vm -> vm.Name.Value = name)
                |> Option.defaultWith (fun () ->
                    let vm =
                        NameViewModel(
                            trim name,
                            isSelected=false,
                            isNewlyDetected=false,
                            isAdded=true)

                    allNames.AddOrUpdate vm
                    vm)

            viewModel.IsSelected <- true

            log Informational AddName name

            this.NewNameToAdd.Value <- ""

    let updateNamesList detectedNames =
        (detectedNames, allNames.Items |> Seq.toList)
        ||> fullOuterJoin toUpper (fun vm -> vm.Name.Value |> toUpper)
        |> Seq.iter (function
            | LeftOnly vm -> vm.IsSelected <- false
            | RightOnly name ->
                NameViewModel(name, true, true, false)
                |> allNames.AddOrUpdate
            | JoinMatch (vm, name) ->
                vm.Name.Value <- name
                vm.IsSelected <- true)

    let updateSelectedFeatures isInitial selectedFeatures =
        (selectedFeatures, featureInstances)
        ||> fullOuterJoin id (fun (vm: FeatureInstanceViewModel) -> vm.CompositeInstanceCode)
        |> Seq.iter (fun result ->
            match result with
            | LeftOnly vm -> vm.IsSelected <- false
            | RightOnly _ -> ()
            | JoinMatch (vm, _) -> vm.IsSelected <- true)

    let getAllNames () =
        allNames.Items
        |> Seq.filter (fun vm -> not vm.IsNewlyDetected.Value)
        |> Seq.map (fun vm -> vm.Name.Value)
        |> Seq.toList

    let updateNewName originalFileName parameters =
        let result = rename parameters originalFileName
        this.NewFileName.Value <- result.NewFileName

        result.DetectedNames
        |> updateNamesList

        result.DetectedFeatures
        |> updateSelectedFeatures (parameters.SelectedFeatures |> Option.isNone)

    let updateResultingFilePath () =
        if not <| isNull this.SelectedDestinationDirectory.Value
        then
            this.SelectedFile.Value
            |> Option.ofObj
            |> Option.iter (fun selectedFile ->
                this.ResultingFilePath.Value <-
                    [|
                        this.SelectedDestinationDirectory.Value.FullName
                        this.NewFileName.Value + Path.getExtension selectedFile.Name
                    |]
                    |> Path.combine)

    let saveSettings baseDirectory =
        if Directory.exists baseDirectory
        then
            {
                SourceDirectoryPrefixes = this.SourceDirectoryPrefixes.Value
                DestinationDirectoryPrefixes = this.DestinationDirectoryPrefixes.Value
                Replacements = this.Replacements |> Seq.toList
                Names =
                    allNames.Items
                    |> Seq.filter (fun vm -> not vm.IsNewlyDetected.Value)
                    |> Seq.map (fun vm -> vm.Name.Value)
                    |> Seq.distinct
                    |> Seq.sort
                    |> Seq.toList
                Features =
                    this.Features
                    |> Seq.map (fun vm -> vm.Feature)
                    |> Seq.toList
            }
            |> Settings.saveSettings baseDirectory

    let loadSettings baseDirectory =
        let settings = Settings.loadSettings baseDirectory

        this.SourceDirectoryPrefixes.Value <- settings.SourceDirectoryPrefixes
        this.DestinationDirectoryPrefixes.Value <- settings.DestinationDirectoryPrefixes

        this.Replacements.Clear()

        settings.Replacements
        |> List.iter this.Replacements.Add

        allNames.Clear()

        settings.Names
        |> List.iter (fun name ->
            NameViewModel(
                name,
                isSelected=false,
                isNewlyDetected=false,
                isAdded=false)
            |> allNames.AddOrUpdate)

        features.Clear()
        featureInstances.Clear()

        settings.Features
        |> List.iter (FeatureViewModel >> this.Features.Add)

        this.Features
        |> Seq.collect (fun vm -> vm.Instances)
        |> this.FeatureInstances.AddRange

    let createSearchTab directory searchString =
        let search = SearchViewModel(searchCommands.AsObservable())
        search.SearchFromBaseDirectory.Value <- true

        selectedFilesSubject.OnNext search.SelectedFileWhenActive
        this.ActiveSearchTab.Value <- search

        directory
        |> Option.iter (fun dir ->
            Directories (Some (DirectoryInfo dir), this.BaseDirectory.Value)
            |> searchCommands.OnNext)

        searchString
        |> Option.iter (fun text ->
            InitialSearchString text |> searchCommands.OnNext
            search.SearchFromBaseDirectory.Value <- true
            search.SearchString.Value <- text)

        searchCommands.OnNext EnableTab

        search

    let toUnderscoreHandling underscoreHandlingEnum =
        match underscoreHandlingEnum with
        | UnderscoreHandling.Ignore -> Ignore
        | UnderscoreHandling.Replace -> Replace
        | UnderscoreHandling.TrimSuffix -> TrimSuffix
        | _ -> failwith "Invalid underscore handling value"

    let parseNames fileName =
        let m = Regex.Match(fileName, @"\(\.(?<names>.+)\.\)")

        if m.Success
        then m.Groups.["names"].Value.Split([| '.' |]) |> Array.toList
        else []

    do
        RxApp.MainThreadScheduler <-
            DispatcherScheduler(Application.Current.Dispatcher)

        refreshDirectoriesCommand <-
            ReactiveCommand.Create(fun () ->
                updateDirectoriesList
                    this.BaseDirectory.Value
                    this.SourceDirectoryPrefixes.Value
                    this.FilterBySourceDirectoryPrefixes.Value)

        selectedFile <-
            selectedFilesSubject
            |> Observable.flatmap id
            |> toReadOnlyReactiveProperty

        saveSettingsCommand <-
            ReactiveCommand.Create(fun () -> saveSettings this.BaseDirectory.Value)

        openCommand <-
            ReactiveCommand.Create(
                (fun (file: FileInfo) -> Process.Start file.FullName |> ignore),
                this.SelectedFile
                |> Observable.map (fun file -> not <| isNull file && file.Exists))

        openFromSearchCommand <-
            ReactiveCommand.Create(fun (file: FileInfo) ->
                if file.Exists then Process.Start file.FullName |> ignore)

        openExplorerCommand <-
            ReactiveCommand.Create(fun (file: FileInfo) ->
                if not <| isNull file
                then
                    file.FullName
                    |> sprintf "/select, \"%s\""
                    |> Some
                elif not <| isNull this.SelectedDirectory.Value
                then
                    this.SelectedDirectory.Value.FullName
                    |> sprintf "\"%s\""
                    |> Some
                else None
                |> Option.iter (asSnd "explorer.exe" >> Process.Start >> ignore))

        showFilePropertiesCommand <-
            ReactiveCommand.Create(fun (file: FileInfo) ->
                if file.Exists then Interop.showFileProperties file.FullName |> ignore)

        deleteFileCommand <-
            ReactiveCommand.Create(fun (file: FileInfo) ->
                if file.Exists
                then
                    MessageBox.Show(
                        sprintf "Delete file %s?" file.FullName,
                        "Delete File",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question)
                    |> function
                        | MessageBoxResult.OK ->
                            try
                                File.delete file.FullName
                                searchCommands.OnNext (Refresh [ file ])
                                log Informational DeleteSuccess file.FullName

                                None
                            with _ ->
                                log Diagnostic DeleteFailure file.FullName

                                Some [ AddDelete file ]
                        | _ -> None
                else None)

        addReplacementCommand <-
            ReactiveCommand.Create(
                (fun () ->
                    {
                        ToReplace = this.ToReplaceToAdd.Value
                        ReplaceWith = this.ReplaceWithToAdd.Value
                    }
                    |> this.Replacements.Add

                    this.ToReplaceToAdd.Value <- ""
                    this.ReplaceWithToAdd.Value <- ""),
                this.ToReplaceToAdd |> Observable.map (String.IsNullOrWhiteSpace >> not))

        clearNewNameToAddCommand <- ReactiveCommand.Create clearNewNameToAdd

        addNameCommand <-
            ReactiveCommand.Create(
                addName,
                this.NewNameToAdd
                |> Observable.map (fun name ->
                    not <| String.IsNullOrWhiteSpace name
                    && not <| name.Contains("|")))

        setNameFilterCommand <-
            ReactiveCommand.Create(
                (fun () ->
                    this.NewNameToAdd.Value <-
                        this.OriginalFileName.Value.Split([| ' '; '_'; '.' |])
                        |> String.concat " | "),
                this.OriginalFileName
                |> Observable.map (String.IsNullOrWhiteSpace >> not))

        resetNameSelectionCommand <- ReactiveCommand.Create(fun () -> ignore ())

        searchForTextCommand <-
            ReactiveCommand.Create(fun name -> createSearchTab None (Some name) |> searches.Add)

        searchForNameCommand <-
            ReactiveCommand.Create(fun name ->
                sprintf ".%s." name |> Some |> createSearchTab None |> searches.Add)

        deleteNameCommand <-
            ReactiveCommand.Create(fun (vm: NameViewModel) ->
                MessageBox.Show(sprintf "Delete name '%s'?" vm.Name.Value,
                                "Delete Name",
                                MessageBoxButton.OKCancel,
                                MessageBoxImage.Question)
                |> function
                    | MessageBoxResult.OK -> allNames.Remove vm |> ignore
                    | _ -> ())

        confirmEditingFeatureCommand <-
            ReactiveCommand.Create(fun () ->
                if not <| String.IsNullOrWhiteSpace this.EditingFeatureName.Value
                    && not <| String.IsNullOrWhiteSpace this.EditingFeatureCode.Value
                then
                    let feature =
                        FeatureViewModel(
                            {
                                Name = this.EditingFeatureName.Value
                                Code = this.EditingFeatureCode.Value
                                Include = nonEmptyString this.EditingFeatureToInclude.Value
                                Instances = []
                            })

                    this.EditingFeatureInstances
                    |> Seq.filter (fun vm ->
                        not <| String.IsNullOrWhiteSpace vm.InstanceName.Value
                        && not <| String.IsNullOrWhiteSpace vm.InstanceCode.Value)
                    |> Seq.iter (fun vm ->
                        let instance =
                            FeatureInstanceViewModel(
                                feature.Feature,
                                {
                                    Name = vm.InstanceName.Value
                                    Code = vm.InstanceCode.Value
                                })

                        feature.Instances.Add instance)

                    this.EditingFeatureName.Value <- ""
                    this.EditingFeatureCode.Value <- ""

                    this.EditingFeatureInstances.Clear()
                    NewFeatureInstanceViewModel() |> this.EditingFeatureInstances.Add

                    let index =
                        this.SelectedFeature.Value
                        |> Option.ofObj
                        |> Option.map (fun feature ->
                            let index = this.Features.IndexOf feature
                            this.Features.Remove feature |> ignore

                            this.SelectedFeature.Value <- Unchecked.defaultof<FeatureViewModel>

                            index)

                    match index |> Option.defaultValue -1 with
                    | -1 -> features.Add feature
                    | index -> features.Insert(index, feature)

                    this.FeatureInstances.Clear()

                    features
                    |> Seq.iter (fun feature -> this.FeatureInstances.AddRange feature.Instances))

        removeFeatureInstanceRowCommand <-
            ReactiveCommand.Create(fun (vm: NewFeatureInstanceViewModel) ->
                this.EditingFeatureInstances.Remove vm |> ignore)

        addFeatureInstanceRowCommand <-
            ReactiveCommand.Create(fun () ->
                NewFeatureInstanceViewModel() |> this.EditingFeatureInstances.Add)

        clearSelectedFeatureCommand <-
            ReactiveCommand.Create(fun () ->
                this.SelectedFeature.Value <- Unchecked.defaultof<FeatureViewModel>)

        expandAllFeaturesCommand <-
            ReactiveCommand.Create(fun () ->
                this.Features |> Seq.iter (fun vm -> vm.IsExpanded.Value <- true))

        collapseAllFeaturesCommand <-
            ReactiveCommand.Create(fun () ->
                this.Features |> Seq.iter (fun vm -> vm.IsExpanded.Value <- false))

        applyCommand <-
            ReactiveCommand.Create<_, _>(
                (fun () ->
                    let oldFile, newName = this.SelectedFile.Value, this.ResultingFilePath.Value

                    try
                        File.move oldFile.FullName newName

                        [ oldFile; FileInfo newName ] |> Refresh |> searchCommands.OnNext

                        sprintf "%s\n%s" oldFile.FullName newName
                        |> log Informational RenameSuccess

                        None
                    with _ ->
                        log Diagnostic RenameFailure oldFile.FullName

                        [ AddRename (oldFile, newName) ] |> Some),
                [
                    this.SelectedFile
                    |> Observable.map (fun file -> not <| isNull file && file.Exists)

                    this.ResultingFilePath
                    |> Observable.map (fun path ->
                        not <| isNull path
                        && (not <| File.exists path
                            || this.SelectedFile.Value.FullName <> path
                                && String.Compare(this.SelectedFile.Value.FullName, path, true) = 0))
                ]
                |> Observable.combineLatestSeq
                |> Observable.map (Seq.toList >> List.forall id))

        reviveFileSystemWatcherCommand <-
            ReactiveCommand.Create<_, _>(fun () ->
                watcher.EnableRaisingEvents <- true
                log Informational KeepAlive "Manually triggered FileSystemWatcher keepalive")

        let removeFilesToChangeSubject = new System.Reactive.Subjects.Subject<_>()

        let fileChangesSubject = new System.Reactive.Subjects.Subject<_>()

        [
            applyCommand |> Observable.choose id

            deleteFileCommand |> Observable.choose id

            removeFilesToChangeSubject |> Observable.observeOn ThreadPoolScheduler.Instance
        ]
        |> Observable.mergeSeq
        |> Observable.filter (List.isEmpty >> not)
        |> Observable.scanInit
            { RenamedFiles = Dictionary(); DeletedFiles = Dictionary() }
            (fun fileChanges changes ->
                (fileChanges, changes)
                ||> List.fold (fun { RenamedFiles = toRename; DeletedFiles = toDelete } change ->
                    match change with
                    | AddRename (oldFile, newName) ->
                        toRename.[oldFile.FullName] <-
                            {
                                OriginalFile = oldFile
                                NewFilePath = newName
                            }
                    | RemoveRename oldNames -> oldNames |> Seq.iter (toRename.Remove >> ignore)
                    | AddDelete file -> toDelete.[file.FullName] <- file
                    | RemoveDelete fileNames -> fileNames |> Seq.iter (toDelete.Remove >> ignore)

                    {
                        RenamedFiles = toRename
                        DeletedFiles = toDelete
                    }))
        |> Observable.subscribeObserver fileChangesSubject
        |> ignore

        fileChangesSubject
        |> Observable.filter (fun fileChanges ->
            fileChanges.RenamedFiles.Any() || fileChanges.DeletedFiles.Any())
        |> Observable.throttle (TimeSpan.FromSeconds 2.)
        |> Observable.subscribe (fun fileChanges ->
            let renamedFiles, IsSome newFiles =
                fileChanges.RenamedFiles.Values
                |> Seq.choose (fun { OriginalFile = oldFile; NewFilePath = newName } ->

                    if File.exists oldFile.FullName
                    then
                        try
                            File.move oldFile.FullName newName

                            sprintf "%s\n%s" oldFile.FullName newName
                            |> log Informational RenameSuccess

                            Some (oldFile, Some (FileInfo newName))
                        with _ -> None
                    else Some (oldFile, None))
                |> Seq.toList
                |> List.unzip

            let deletedFiles =
                fileChanges.DeletedFiles.Values
                |> Seq.choose (fun file ->
                    try
                        File.delete file.FullName

                        log Informational DeleteSuccess file.FullName

                        Some file
                    with _ -> None)
                |> Seq.toList

            [
                renamedFiles |> List.map (fun file -> file.FullName) |> RemoveRename
                deletedFiles |> List.map (fun file -> file.FullName) |> RemoveDelete
            ]
            |> removeFilesToChangeSubject.OnNext

            [ renamedFiles; deletedFiles; newFiles ]
            |> List.concat
            |> function
                | [] -> ()
                | files ->
                    files
                    |> Refresh
                    |> searchCommands.OnNext)
        |> ignore

        fileChanges <-
            fileChangesSubject.ToReadOnlyReactiveProperty(
                mode = ReactivePropertyMode.RaiseLatestValueOnSubscribe)

        this.SelectedDirectory
        |> Observable.map (fun dir ->
            Directories (dir |> Option.ofObj |> Option.map (fun dir -> dir.DirectoryInfo), this.BaseDirectory.Value))
        |> Observable.subscribeObserver searchCommands
        |> ignore

        this.ActiveSearchTab
        |> Observable.subscribe (fun tab ->
            searches
            |> Seq.iter (fun vm -> vm.IsActive.Value <- (tab = vm)))
        |> ignore

        let validBaseDirectory =
            this.BaseDirectory
            |> Observable.throttle (TimeSpan.FromMilliseconds 500.)
            |> Observable.filter Directory.exists

        validBaseDirectory
        |> Observable.observeOn RxApp.MainThreadScheduler
        |> Observable.subscribe (fun dir ->
            loadSettings dir
            searchCommands.OnNext (Directories (None, dir)))
        |> ignore

        isBaseDirectoryValid <-
            this.BaseDirectory
            |> Observable.combineLatest validBaseDirectory
            |> Observable.map (fun (``base``, validBase) -> ``base`` = validBase)
            |> toReadOnlyReactiveProperty

        validBaseDirectory
        |> Observable.map (fun directory ->
            let scanned =
                Directory.enumerateFiles directory
                |> Seq.map (asFst Added >> List.singleton)
                |> Observable.ofSeq

            let watched =
                Observable.Create(fun observer ->
                    watcher.Path <- directory
                    watcher.EnableRaisingEvents <- true

                    [
                        watcher.Created |> Observable.map (fun e -> [ e.FullPath, Added ])
                        watcher.Deleted |> Observable.map (fun e -> [ e.FullPath, Removed ])
                        watcher.Renamed
                        |> Observable.map (fun e -> [ e.OldFullPath, Removed; e.FullPath, Added ])
                    ]
                    |> Observable.mergeSeq
                    |> Observable.subscribeObserver observer)

            scanned
            |> Observable.concat watched
            |> Observable.filter (List.isEmpty >> not)
            |> Observable.map (fun changes ->
                changes
                |> List.collect (fun (filePath, change) ->
                    Path.getFileNameWithoutExtension filePath
                    |> parseNames
                    |> List.map (asFst change)))
            |> Observable.scanInit (Map.empty, []) (fun (acc, _) current ->
                let updates =
                    current
                    |> List.map (fun (name, change) ->
                        acc
                        |> Map.tryFind name
                        |> Option.map (fun count ->
                            name, (if change = Added then count + 1 else count - 1))
                        |> Option.defaultValue (name, 1))

                (acc, updates) ||> List.fold (fun map (name, count) -> map |> Map.add name count), updates))
        |> Observable.switch
        |> Observable.subscribe (fun (_, updates) ->
            updates
            |> List.iter (fun (name, count) ->
                allNames.Items
                |> Seq.tryFind (fun vm -> vm.Name.Value = name)
                |> Option.iter (fun vm -> vm.Count.Value <- count)))
        |> ignore

        this.SourceDirectoryPrefixes
        |> Observable.throttle (TimeSpan.FromMilliseconds 500.)
        |> Observable.combineLatest this.FilterBySourceDirectoryPrefixes
        |> Observable.combineLatest validBaseDirectory
        |> Observable.observeOn RxApp.MainThreadScheduler
        |> Observable.subscribe (fun (baseDirectory, (filterByPrefixes, prefixes)) ->
            updateDirectoriesList baseDirectory prefixes filterByPrefixes)
        |> ignore

        createSearchTab None None |> searches.Add

        let existingSelectedFile = this.SelectedFile |> Observable.filter (isNull >> not)

        existingSelectedFile
        |> Observable.map (fun file -> file.FullName)
        |> Observable.combineLatest
            (this.DestinationDirectoryPrefixes
             |> Observable.throttle (TimeSpan.FromMilliseconds 500.)
             |> Observable.observeOn RxApp.MainThreadScheduler)
        |> Observable.subscribe (uncurry updateDestinationDirectories)
        |> ignore

        existingSelectedFile
        |> Observable.subscribe (fun file ->
            allNames.Items
            |> Seq.filter (fun vm -> vm.IsNewlyDetected.Value)
            |> Seq.toList
            |> List.iter allNames.Remove

            this.OriginalFileName.Value <-
                string file.Name |> Path.getFileNameWithoutExtension

            this.NewNameToAdd.Value <- "")
        |> ignore

        [
            this.OriginalFileNameSelectedText :> IObservable<_>
            this.NewFileNameSelectedText :> IObservable<_>
        ]
        |> Observable.mergeSeq
        |> Observable.throttleOn RxApp.MainThreadScheduler (TimeSpan.FromMilliseconds 500.)
        |> Observable.subscribe (fun text ->
            this.NewNameToAdd.Value <-
                if this.RecapitalizeNames.Value
                then toTitleCase text
                else text)
        |> ignore

        NewFeatureInstanceViewModel()
        |> this.EditingFeatureInstances.Add

        let updateNewNameGate = new BooleanNotifier(true)

        let allNamesChanges = allNames.Connect().WhenPropertyChanged(fun vm -> vm.IsSelected)

        [
            this.TreatParenthesizedPartAsNames |> Observable.map TreatParenthesizedPartAsNames
            this.FixupNamesInMainPart |> Observable.map FixupNamesInMainPart
            this.UnderscoreHandling
            |> Observable.map (toUnderscoreHandling >> Logic.UnderscoreHandling)

            this.DetectNamesInMainAndNamesParts |> Observable.map DetectNamesInMainAndNamesParts
            this.RecapitalizeNames |> Observable.map RecapitalizeNames

            allNamesChanges
            |> xwhen updateNewNameGate
            |> Observable.map (fun _ ->
                allNames.Items
                |> Seq.filter (fun n -> n.IsSelected)
                |> Seq.map (fun n -> n.Name.Value)
                |> Seq.toList
                |> Some
                |> SelectedNames)

            this.ResetNameSelectionCommand.IsExecuting
            |> Observable.distinctUntilChanged
            |> Observable.filter id
            |> Observable.map (fun _ -> SelectedNames None)

            this.OriginalFileName |> Observable.map (fun _ -> ResetSelections)

            this.FeatureInstances.ItemChanged
            |> Observable.filter (fun change ->
                change.PropertyName = nameof Unchecked.defaultof<FeatureInstanceViewModel>.IsSelected)
            |> xwhen updateNewNameGate
            |> Observable.iter (fun change ->
                if change.Sender.IsSelected
                then
                    updateNewNameGate.TurnOff()

                    change.Sender.Include
                    |> Option.iter (fun toInclude ->
                        let featureToInclude =
                            this.FeatureInstances
                            |> Seq.tryFind (fun vm -> vm.CompositeInstanceCode = toInclude)

                        featureToInclude
                        |> Option.iter (fun vm -> vm.IsSelected <- true))

                    updateNewNameGate.TurnOn())
            |> Observable.map (fun _ ->
                this.SelectedFeatureInstances
                |> Seq.toList
                |> Some
                |> SelectedFeatures)
        ]
        |> Observable.mergeSeq
        |> Observable.scanInit
            {
                TreatParenthesizedPartAsNames = this.TreatParenthesizedPartAsNames.Value
                FixupNamesInMainPart = this.FixupNamesInMainPart.Value
                RecapitalizeNames = this.RecapitalizeNames.Value
                UnderscoreHandling = this.UnderscoreHandling.Value |> toUnderscoreHandling
                DetectNamesInMainAndNamesParts = this.DetectNamesInMainAndNamesParts.Value
                SelectedNames = None
                SelectedFeatures = None
                Replacements = []
                AllNames = getAllNames ()
            }
            (updateParameters this.Replacements getAllNames)
        |> Observable.subscribe (fun parameters ->
            updateNewNameGate.TurnOff()
            updateNewName this.OriginalFileName.Value parameters
            updateNewNameGate.TurnOn())
        |> ignore

        let nameFilter =
            this.NewNameToAdd
            |> Observable.throttleOn ThreadPoolScheduler.Instance (TimeSpan.FromMilliseconds 500.)
            |> Observable.map (fun name ->
                if String.IsNullOrWhiteSpace name
                then Func<_, _> (fun _ -> true)
                elif name.Contains "|"
                then
                    let parts = (toUpper name).Split [| '|' |] |> Seq.map trim |> Set.ofSeq

                    Func<_, _> (fun (n: NameViewModel) ->
                        let nameParts = n.Name.Value.ToUpper().Split [| ' ' |] |> Set.ofSeq
                        parts |> Set.intersect nameParts |> (<>) Set.empty)
                else
                    let up = toUpper name

                    Func<_, _> (fun (n: NameViewModel) -> n.Name.Value.ToUpper().Contains up))

        allNames.Connect()
            .AutoRefresh()
            .Filter(fun vm -> vm.IsSelected || vm.IsPinned)
            .Sort(NameViewModelComparer Selection)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(&selectedNames)
            .Subscribe()
        |> ignore

        allNames.Connect()
            .AutoRefresh()
            .Filter(nameFilter)
            .Sort(NameViewModelComparer NameOnly)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(&names)
            .Subscribe()
        |> ignore

        this.NewFileName
        |> Observable.combineLatest this.SelectedDestinationDirectory
        |> Observable.observeOn RxApp.MainThreadScheduler
        |> Observable.subscribe (fun _ -> updateResultingFilePath ())
        |> ignore

        this.SelectedFeature
        |> Observable.subscribe (fun (OfNull feature) ->
            this.EditingFeatureInstances.Clear()

            let featureName, featureCode, toInclude =
                feature
                |> Option.map (fun feature -> feature.FeatureName, feature.FeatureCode, feature.Include)
                |> Option.defaultValue ("", "", None)

            this.EditingFeatureName.Value <- featureName
            this.EditingFeatureCode.Value <- featureCode
            this.EditingFeatureToInclude.Value <- toInclude |> Option.defaultValue ""

            feature
            |> Option.bind (fun feature ->
                feature.Instances
                |> Seq.map (fun instance -> instance.Instance)
                |> Seq.toList
                |> function
                    | [] -> None
                    | instances ->
                        instances
                        |> List.map NewFeatureInstanceViewModel
                        |> Some)
            |> Option.defaultWith (fun () -> [ NewFeatureInstanceViewModel() ])
            |> List.iter this.EditingFeatureInstances.Add)
        |> ignore

        groupCategories <-
            features.Changed
            |> Observable.map (fun _ ->
                [
                    { Name = "No grouping"; GroupCategory = NoGrouping }
                    { Name = "Individual names"; GroupCategory = ByIndividualNames }
                    { Name = "Co-occurring names"; GroupCategory = ByCoOccurringNames }

                    yield!
                        features
                        |> Seq.map (fun vm ->
                            {
                                Name = "> " + vm.Feature.Name
                                GroupCategory = ByFeature vm.Feature
                            })
                        |> Seq.toList
                ])
            |> toReadOnlyReactiveProperty

        isFileSystemWatcherAlive <-
            Observable.interval (TimeSpan.FromSeconds 1.)
            |> Observable.map (fun _ -> watcher.EnableRaisingEvents)
            |> Observable.scanInit (false, AlreadyDead) (fun (previousValue, _) newValue ->
                newValue,
                match previousValue, newValue with
                | false, true -> NewlyAlive
                | true, true -> AlreadyAlive
                | true, false -> NewlyDead
                | false, false -> AlreadyDead)
            |> Observable.map (fun (_, status) ->
                let isAlive, logMessage =
                    match status with
                    | AlreadyAlive -> true, None
                    | NewlyAlive -> true, Some "FileSystemWatcher is alive"
                    | _ ->
                        if not <| isNull watcher.Path && Directory.exists watcher.Path
                        then
                            watcher.EnableRaisingEvents <- true

                            true, Some "FileSystemWatcher revived"
                        elif status = NewlyDead
                        then false, Some "FileSystemWatcher is dead"
                        else false, None

                logMessage |> Option.iter (log Informational KeepAlive)

                isAlive)
            |> toReadOnlyReactiveProperty

        activityLogSubject
        |> Observable.observeOn RxApp.MainThreadScheduler
        |> Observable.subscribeOn RxApp.MainThreadScheduler
        |> Observable.subscribe activityLog.Add
        |> ignore

    member __.Shutdown () = saveSettings __.BaseDirectory.Value

    member __.HasTouchInput = hasTouchInput

    member __.BaseDirectory: ReactiveProperty<string> = baseDirectory
    member __.FilterBySourceDirectoryPrefixes: ReactiveProperty<_> = filterBySourceDirectoryPrefixes
    member __.SourceDirectoryPrefixes: ReactiveProperty<_> = sourceDirectoryPrefixes
    member __.RefreshDirectoriesCommand = refreshDirectoriesCommand

    member __.SelectedDirectory: ReactiveProperty<DirectoryViewModel> = selectedDirectory

    member __.Directories = directories

    member __.Searches = searches
    member __.CreateSearchTab = Func<_>(fun () -> createSearchTab None None)
    member __.CreateSearchTabForDirectory(directory: string) =
        createSearchTab (Some directory) None |> searches.Add
    member __.ActiveSearchTab: ReactiveProperty<SearchViewModel> = activeSearchTab
    member __.IsBaseDirectoryValid: ReadOnlyReactiveProperty<_> = isBaseDirectoryValid
    member __.CloseSearchTabCallback =
        ItemActionCallback(fun (args: ItemActionCallbackArgs<TabablzControl>) ->
            if args.Owner.Items.Count < 2 then args.Cancel())

    member __.SelectedFile: ReadOnlyReactiveProperty<FileInfo> = selectedFile

    member __.SaveSettingsCommand = saveSettingsCommand

    member __.OriginalFileName: ReactiveProperty<string> = originalFileName
    member __.OriginalFileNameSelectedText: ReactiveProperty<_> = originalFileNameSelectedText
    member __.NewFileName: ReactiveProperty<string> = newFileName
    member __.NewFileNameSelectedText: ReactiveProperty<_> = newFileNameSelectedText

    member __.TreatParenthesizedPartAsNames: ReactiveProperty<_> = treatParenthesizedPartAsNames
    member __.FixupNamesInMainPart: ReactiveProperty<_> = fixupNamesInMainPart
    member __.UnderscoreHandling: ReactiveProperty<_> = underscoreHandling
    member __.DetectNamesInMainAndNamesParts: ReactiveProperty<_> = detectNamesInMainAndNamesParts
    member __.RecapitalizeNames: ReactiveProperty<_> = recapitalizeNames

    member __.OpenCommand = openCommand
    member __.OpenFromSearchCommand = openFromSearchCommand
    member __.OpenExplorerCommand = openExplorerCommand
    member __.ShowFilePropertiesCommand = showFilePropertiesCommand
    member __.DeleteFileCommand = deleteFileCommand

    member __.FileChanges = fileChanges

    member __.SelectedDestinationDirectory: ReactiveProperty<_> = selectedDestinationDirectory
    member __.DestinationDirectoryPrefixes: ReactiveProperty<_> = destinationDirectoryPrefixes
    member __.DestinationDirectories = destinationDirectories

    member __.ToReplaceToAdd: ReactiveProperty<_> = toReplaceToAdd
    member __.ReplaceWithToAdd: ReactiveProperty<_> = replaceWithToAdd
    member __.AddReplacementCommand = addReplacementCommand
    member __.Replacements: ObservableCollection<_> = replacements

    member __.NewNameToAdd: ReactiveProperty<string> = newNameToAdd
    member __.ClearNewNameToAdd() = clearNewNameToAdd ()
    member __.ClearNewNameToAddCommand = clearNewNameToAddCommand
    member __.AddName(name: string) = addName name
    member __.AddNameCommand = addNameCommand
    member __.SetNameFilterCommand = setNameFilterCommand

    member __.SelectedNames: ReadOnlyObservableCollection<NameViewModel> = selectedNames
    member __.Names: ReadOnlyObservableCollection<NameViewModel> = names

    member __.ResetNameSelectionCommand: ReactiveCommand = resetNameSelectionCommand
    member __.SearchForTextCommand = searchForTextCommand
    member __.SearchForNameCommand = searchForNameCommand
    member __.DeleteNameCommand = deleteNameCommand

    member __.EditingFeatureInstances: ObservableCollection<NewFeatureInstanceViewModel> = editingFeatureInstances
    member __.RemoveFeatureInstanceRowCommand = removeFeatureInstanceRowCommand
    member __.AddFeatureInstanceRowCommand = addFeatureInstanceRowCommand
    member __.ClearSelectedFeatureCommand = clearSelectedFeatureCommand
    member __.AddFeatureInstanceRow() =
        NewFeatureInstanceViewModel() |> this.EditingFeatureInstances.Add
    member __.ExpandAllFeaturesCommand = expandAllFeaturesCommand
    member __.CollapseAllFeaturesCommand = collapseAllFeaturesCommand

    member __.EditingFeatureName: ReactiveProperty<string> = editingFeatureName
    member __.EditingFeatureCode: ReactiveProperty<string> = editingFeatureCode
    member __.EditingFeatureToInclude: ReactiveProperty<string> = editingFeatureToInclude

    member __.ConfirmEditingFeatureCommand = confirmEditingFeatureCommand

    member __.SelectedFeature: ReactiveProperty<FeatureViewModel> = selectedFeature

    member __.SelectedFeatureInstances =
        this.FeatureInstances
        |> Seq.filter (fun vm -> vm.IsSelected)
        |> Seq.map (fun vm -> vm.FeatureCode + vm.InstanceCode)

    member __.Features: ReactiveList<FeatureViewModel> = features
    member __.FeatureInstances: ReactiveList<FeatureInstanceViewModel> = featureInstances
    member __.GroupCategories = groupCategories

    member __.EnableFeatureEditing: ReactiveProperty<bool> = enableFeatureEditing

    member __.ResultingFilePath: ReactiveProperty<string> = resultingFilePath

    member __.ApplyCommand = applyCommand

    member __.ReviveFileSystemWatcherCommand = reviveFileSystemWatcherCommand
    member __.IsFileSystemWatcherAlive = isFileSystemWatcherAlive

    member __.ActivityLog = activityLog
