﻿namespace TeaDriven.Zokhion.ViewModels

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
open DynamicData.Binding

open FSharp.Control.Reactive

open ReactiveUI

open Reactive.Bindings
open Reactive.Bindings.Notifiers

open TeaDriven.Zokhion

type ReactiveCommand = ReactiveUI.ReactiveCommand

[<AutoOpen>]
module Utility =
    open System.Linq.Expressions
    open FSharp.Quotations

    // see https://stackoverflow.com/a/48311816/236507
    let nameof (q: Expr<_>) =
        match q with
        | Patterns.Let(_, _, DerivedPatterns.Lambdas(_, Patterns.Call(_, mi, _))) -> mi.Name
        | Patterns.PropertyGet(_, mi, _) -> mi.Name
        | DerivedPatterns.Lambdas(_, Patterns.Call(_, mi, _)) -> mi.Name
        | _ -> failwith "Unexpected format"

    let any<'R> : 'R = failwith "!"

    // From http://stackoverflow.com/questions/2682475/converting-f-quotations-into-linq-expressions
    /// Converts a F# Expression to a LINQ Lambda
    let toLambda (exp: Expr) =
        let linq =
            FSharp.Linq.RuntimeHelpers.LeafExpressionConverter.QuotationToExpression exp :?> MethodCallExpression
        linq.Arguments.[0] :?> LambdaExpression

    /// Converts a Lambda quotation into a Linq Lambda Expression with 1 parameter
    let toLinq (exp: Expr<'a -> 'b>) =
        let lambda = toLambda exp
        Expression.Lambda<Func<'a, 'b>>(lambda.Body, lambda.Parameters)

    let withLatestFrom (observable2: IObservable<'T2>) (observable1: IObservable<'T1>) =
        observable1.WithLatestFrom(observable2, fun v1 v2 -> v1, v2)

    let xwhen (observable2: IObservable<_>) (observable1: IObservable<_>) =
        observable1 |> withLatestFrom observable2 |> Observable.filter snd |> Observable.map fst

    let containsAll parts (s: string) = parts |> List.forall s.Contains

    let split (separators: string []) (s: string) =
        s.Split(separators, StringSplitOptions.RemoveEmptyEntries)

    let toReadOnlyReactiveProperty (observable: IObservable<_>) =
        observable.ToReadOnlyReactiveProperty()

    let maxString maxLength (s: string) = s.Substring(0, Math.Min(s.Length, maxLength))

[<AllowNullLiteral>]
type FeatureViewModel(feature: Feature) as this =
    inherit ReactiveObject()

    let instances = ReactiveList<FeatureInstanceViewModel>(ChangeTrackingEnabled = true)

    let mutable hasSelectedInstances = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    do
        match this with
        | :? FeatureInstanceViewModel -> ()
        | _ ->
            feature.Instances
            |> List.map (fun instance -> FeatureInstanceViewModel(feature, instance))
            |> instances.AddRange

        hasSelectedInstances <-
            instances.ItemChanged
            |> Observable.filter (fun change ->
                change.PropertyName = nameof <@ any<FeatureInstanceViewModel>.IsSelected @>)
            |> Observable.map (fun _ ->
                instances |> Seq.exists (fun instance -> instance.IsSelected))
            |> toReadOnlyReactiveProperty

    member __.FeatureName = feature.Name

    member __.FeatureCode = feature.Code

    member __.Include = feature.Include

    member __.Instances = instances

    member __.HasSelectedInstances = hasSelectedInstances

    member __.Feature =
        { feature with Instances = instances |> Seq.map (fun vm -> vm.Instance) |> Seq.toList }

    member val IsExpanded = new ReactiveProperty<_>(true)

    member __.ResetExpanded () = ()
        // __.IsExpanded.Value <- __.Instances |> Seq.exists (fun vm -> vm.IsSelected)

and [<AllowNullLiteral>]
    FeatureInstanceViewModel(feature: Feature, instance: FeatureInstance) =
    inherit FeatureViewModel(feature)

    let mutable isSelected = false

    member __.InstanceName = instance.Name

    member __.InstanceCode = instance.Code

    member __.CompositeInstanceCode = feature.Code + instance.Code

    member __.IsSelected
        with get () = isSelected
        and set value = __.RaiseAndSetIfChanged(&isSelected, value, nameof <@ __.IsSelected @>) |> ignore

    member __.Instance = instance

//type FeatureInstanceIncomingUpdate = X

//type FeatureInstanceOutgoingUpdate =
//    | Change of NewFeatureInstanceViewModel * FeatureInstance

//and NewFeatureInstanceViewModel(instanceName: string,
//                                instanceCode: string,
//                                incomingUpdates: IObservable<FeatureInstanceIncomingUpdate>) as this =

//    let instanceName = new ReactiveProperty<_>(instanceName)
//    let instanceCode = new ReactiveProperty<_>(instanceCode)

//    let mutable outgoingUpdates = Unchecked.defaultof<IObservable<FeatureInstanceOutgoingUpdate>>

//    do
//        outgoingUpdates <-
//            Observable.combineLatest instanceName instanceCode
//            |> Observable.throttleOn RxApp.MainThreadScheduler (TimeSpan.FromMilliseconds 500.)
//            |> Observable.map (fun (name, code) ->
//                Change (this, { Name = name; Code = code }))

//    member __.InstanceName = instanceName
//    member __.InstanceCode = instanceCode

//    member __.Updates = outgoingUpdates

//    new (incomingUpdates: IObservable<FeatureInstanceIncomingUpdate>) =
//        NewFeatureInstanceViewModel("", "", incomingUpdates)

type NewFeatureInstanceViewModel(instanceName: string, instanceCode: string) =
    let instanceName = new ReactiveProperty<_>(instanceName)
    let instanceCode = new ReactiveProperty<_>(instanceCode)

    do
        instanceName
        |> withLatestFrom instanceCode
        |> Observable.filter (snd >> String.IsNullOrWhiteSpace)
        |> Observable.map fst
        |> Observable.subscribe (fun name -> instanceCode.Value <- maxString 2 name)
        |> ignore

    member __.InstanceName = instanceName
    member __.InstanceCode = instanceCode

    new () = NewFeatureInstanceViewModel("", "")

    new (feature: FeatureInstance) =
        NewFeatureInstanceViewModel(feature.Name, feature.Code)

[<AllowNullLiteral>]
type NameViewModel(name: string, isSelected: bool, isNewlyDetected: bool, isAdded: bool) =
    inherit AbstractNotifyPropertyChanged()

    let mutable xIsSelected = isSelected

    let mutable isPinned = false

    member val Name = new ReactiveProperty<_>(name)

    member __.IsSelected
        with get () = xIsSelected
        and set value = __.SetAndRaise(&xIsSelected, value, nameof <@ __.IsSelected @>)

    member __.IsPinned
        with get () = isPinned
        and set value = __.SetAndRaise(&isPinned, value, nameof <@ __.IsPinned @>)

    member val IsNewlyDetected = new ReactiveProperty<_>(isNewlyDetected)

    member val IsAdded = new ReactiveProperty<_>(isAdded)

    member val Count = new ReactiveProperty<_>(0)

    member __.ClearNewFlagCommand =
        ReactiveCommand.Create(fun () ->
            __.IsNewlyDetected.Value <- false
            __.IsAdded.Value <- true)

    member __.PinCommand =
        ReactiveCommand.Create(fun () -> __.IsPinned <- not __.IsPinned)

type NameViewModelComparer() =
    interface IComparer<NameViewModel> with
        member __.Compare (a, b) =
            let byFlag flagA flagB =
                match flagA, flagB with
                | true, false -> Some -1
                | false, true -> Some 1
                | _ -> None

            byFlag a.IsSelected b.IsSelected
            |> Option.defaultWith (fun () ->
                byFlag a.IsNewlyDetected.Value b.IsNewlyDetected.Value
                |> Option.defaultWith (fun () ->
                    byFlag a.IsPinned b.IsPinned
                    |> Option.defaultWith (fun () -> a.Name.Value.CompareTo b.Name.Value)))

type SearchViewModelCommand =
    | Directories of (DirectoryInfo option * string)
    | Refresh of FileInfo list

type SearchCriterion =
    | Contains of string list
    | SmallerThan of int
    | LargerThan of int

type WithFeatures = HasNoFeatures | HasFeatures | Both

type SearchFilterParameters =
    {
        BaseDirectory: string
        SelectedDirectory: string option
        SearchValues: string list
        WithFeatures: WithFeatures
        SearchFromBaseDirectory: bool
    }

type FilterMode = Search | CheckAffected

type SearchFilter =
    {
        Filter: FilterMode -> FileInfo -> bool
        SearchDirectory: string option
    }

type SearchFilterChange =
    | BaseDirectory of string
    | SelectedDirectory of string
    | SearchValues of string list
    | SearchFromBaseDirectory of bool
    | WithFeatures of WithFeatures

type RenamedFile = { OriginalFile: FileInfo; NewFilePath: string }

type FileChanges =
    {
        RenamedFiles: Dictionary<string, RenamedFile>
        DeletedFiles: Dictionary<string, FileInfo>
    }

type SearchViewModel(commands: IObservable<SearchViewModelCommand>) as this =
    inherit ReactiveObject()

    let mutable baseDirectory = ""
    let selectedDirectory = new BehaviorSubject<DirectoryInfo option>(None)

    let searchString = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let searchFromBaseDirectory = new ReactiveProperty<_>(true)
    let mutable canToggleSearchFromBaseDirectory =
        Unchecked.defaultof<ReadOnlyReactiveProperty<bool>>
    let isActive = new ReactiveProperty<_>(true)
    let files = ObservableCollection()
    let mutable header = Unchecked.defaultof<ReadOnlyReactiveProperty<string>>
    let selectedFile = new ReactiveProperty<FileInfo>()
    let mutable selectedFileWhenActive = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>
    let mutable refreshCommand = Unchecked.defaultof<ReactiveCommand<_, _>>
    let mutable clearSearchStringCommand = Unchecked.defaultof<ReactiveCommand>

    let refreshSubject = new System.Reactive.Subjects.Subject<_>()

    let filterHasNoFeatures = new ReactiveProperty<_>(false)
    let filterHasFeatures = new ReactiveProperty<_>(false)

    let isUpdatingNotifier = BooleanNotifier(false)
    let mutable isUpdating = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    let mutable filter = Unchecked.defaultof<ReadOnlyReactiveProperty<_>>

    let smallerThanRegex = Regex(@"^<\s*(?<size>\d+)MB$", RegexOptions.Compiled)
    let largerThanRegex = Regex(@"^>\s*(?<size>\d+)MB$", RegexOptions.Compiled)
    let hasFeaturesRegex = Regex(@"^.+\[\..+\.\]$", RegexOptions.Compiled)

    let createFilter searchFilterParameters =
        let searchDirectory =
            if searchFilterParameters.SearchFromBaseDirectory
               && searchFilterParameters.SearchValues <> []
            then Some searchFilterParameters.BaseDirectory
            else searchFilterParameters.SelectedDirectory

        let searchFilters =
            let filters =
                [
                    yield!
                        match searchFilterParameters.SearchValues with
                        | [] -> [ Some (fun _ -> true) ]
                        | _ ->
                            let smaller, contains, larger =
                                searchFilterParameters.SearchValues
                                |> List.map (fun part ->
                                    let m = smallerThanRegex.Match part

                                    if m.Success
                                    then m.Groups.["size"].Value |> Int32.Parse |> (*) (1024 * 1024) |> SmallerThan
                                    else
                                        let m = largerThanRegex.Match part

                                        if m.Success
                                        then m.Groups.["size"].Value |> Int32.Parse |> (*) (1024 * 1024) |> LargerThan
                                        else Contains [ toUpper part ])
                                |> asSnd (None, [], None)
                                ||> List.fold (fun (smaller, contains, larger) current ->
                                    match current with
                                    | SmallerThan smaller -> Some smaller, contains, larger
                                    | Contains parts -> smaller, parts @ contains, larger
                                    | LargerThan larger -> smaller, contains, Some larger)

                            [
                                yield
                                    smaller
                                    |> Option.map (fun smaller -> fun (fi: FileInfo) -> fi.Length < int64 smaller)

                                yield
                                    larger
                                    |> Option.map (fun larger -> fun (fi: FileInfo) -> fi.Length > int64 larger)

                                yield
                                    match contains with
                                    | [] -> None
                                    | contains ->
                                        (fun (fi: FileInfo) ->
                                            fi.Name
                                            |> Path.GetFileNameWithoutExtension
                                            |> toUpper
                                            |> (fun s -> [ s; s.Replace("_", " ") ])
                                            |> Seq.exists (containsAll contains))
                                            |> Some
                            ]

                    let checkHasFeatures (fi: FileInfo) =
                        fi.FullName
                        |> Path.GetFileNameWithoutExtension
                        |> hasFeaturesRegex.IsMatch

                    yield
                        match searchFilterParameters.WithFeatures with
                        | HasNoFeatures -> Some (checkHasFeatures >> not)
                        | HasFeatures -> Some checkHasFeatures
                        | Both -> None
                ]
                |> List.choose id

            fun (fi: FileInfo) -> filters |> Seq.forall (fun filter -> filter fi)

        let filter =
            let checkFilter =
                fun (fi: FileInfo) ->
                    (toUpper fi.FullName).StartsWith(searchDirectory |> Option.defaultValue "" |> toUpper)
                    && searchFilters fi

            fun filterMode (fi: FileInfo) ->
                match filterMode with
                | Search -> searchFilters fi
                | CheckAffected -> checkFilter fi

        {
            Filter = filter
            SearchDirectory = searchDirectory
        }

    let getFiles filter =
        filter.SearchDirectory
        |> Option.filter (String.IsNullOrWhiteSpace >> not <&&> Directory.Exists)
        |> Option.map (fun dir ->
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            |> Seq.map FileInfo
            |> Seq.filter (filter.Filter Search))

    do
        header <-
            searchString
            |> Observable.map (function
                | "" ->
                    selectedDirectory.Value
                    |> Option.map (fun selected -> selected.Name)
                    |> Option.defaultValue " (none) "
                    |> sprintf "<%s>"
                | search -> search)
            |> Observable.startWith [ "Search" ]
            |> toReadOnlyReactiveProperty

        canToggleSearchFromBaseDirectory <-
            selectedDirectory
            |> Observable.map Option.isSome
            |> toReadOnlyReactiveProperty

        clearSearchStringCommand <- ReactiveCommand.Create(fun () -> searchString.Value <- "")

        refreshCommand <- ReactiveCommand.Create<_>(fun () -> Refresh [])

        isUpdating <-
            [
                isUpdatingNotifier |> Observable.filter not

                isUpdatingNotifier |> Observable.throttle (TimeSpan.FromSeconds 1.5)
            ]
            |> Observable.mergeSeq
            |> toReadOnlyReactiveProperty

        filter <-
            [
                commands
                |> Observable.filter (fun _ -> isActive.Value)
                |> Observable.map (function
                    | Directories (dir, ``base``) ->
                        [
                            dir |> Option.map (fun di -> SelectedDirectory di.FullName)

                            Some (BaseDirectory ``base``)
                        ]
                        |> List.choose id
                    | Refresh _ -> [])

                searchString
                |> Observable.throttleOn RxApp.MainThreadScheduler (TimeSpan.FromMilliseconds 500.)
                |> Observable.map
                    (toUpper
                     >> split [| "&&" |]
                     >> Array.map trim
                     >> Array.toList
                     >> SearchValues
                     >> List.singleton)
                |> Observable.distinctUntilChanged

                searchFromBaseDirectory |> Observable.map (SearchFromBaseDirectory >> List.singleton)

                Observable.combineLatest filterHasNoFeatures filterHasFeatures
                |> Observable.map (fun flags ->
                    match flags with
                    | true, false -> HasNoFeatures
                    | false, true -> HasFeatures
                    | _ -> Both
                    |> WithFeatures
                    |> List.singleton)

                refreshSubject |> Observable.map (fun () -> [])
            ]
            |> Observable.mergeSeq
            |> Observable.scanInit
                {
                    BaseDirectory = ""
                    SelectedDirectory = None
                    SearchValues = []
                    WithFeatures = Both
                    SearchFromBaseDirectory = searchFromBaseDirectory.Value
                }
                (fun parameters changes ->
                    (parameters, changes)
                    ||> List.fold (fun current change ->
                        match change with
                        | BaseDirectory ``base`` -> { current with BaseDirectory = ``base`` }
                        | SelectedDirectory dir -> { current with SelectedDirectory = Some dir }
                        | SearchValues searchValues -> { current with SearchValues = searchValues }
                        | WithFeatures withFeatures -> { current with WithFeatures = withFeatures }
                        | SearchFromBaseDirectory fromBase ->
                            { current with SearchFromBaseDirectory = fromBase }))
            |> Observable.map createFilter
            |> toReadOnlyReactiveProperty

        filter
        |> Observable.iter (fun _ -> isUpdatingNotifier.TurnOn())
        |> Observable.observeOn ThreadPoolScheduler.Instance
        |> Observable.choose getFiles
        |> Observable.observeOn RxApp.MainThreadScheduler
        |> Observable.subscribe (fun newFiles ->
            (newFiles, (files |> Seq.toList))
            ||> fullOuterJoin (fun fi -> fi.FullName) (fun (fi: FileInfo) -> fi.FullName)
            |> Seq.iter (function
                | LeftOnly fi -> files.Remove fi |> ignore
                | RightOnly fi -> files.Add fi
                | JoinMatch (old, ``new``) ->
                    if (``new``.Length, ``new``.LastWriteTimeUtc)
                       <> (old.Length, old.LastWriteTimeUtc)
                    then
                        files.Remove old |> ignore
                        files.Add ``new``)

            isUpdatingNotifier.TurnOff()

            this.RaisePropertyChanged(nameof <@ this.Files @>))
        |> ignore

        [ refreshCommand.AsObservable(); commands ]
        |> Observable.mergeSeq
        |> Observable.subscribe (function
            | Directories (selected, ``base``) ->
                if isActive.Value
                then
                    selectedDirectory.OnNext selected
                    baseDirectory <- ``base``
                    searchFromBaseDirectory.Value <- Option.isNone selected

                    searchString.Value <- ""
            | Refresh files ->
                match files with
                | [] -> isActive.Value
                | _ -> files |> List.exists (filter.Value.Filter CheckAffected)
                |> fun refresh -> if refresh then refreshSubject.OnNext ())
        |> ignore

        isActive
        |> Observable.filter id
        |> Observable.subscribe (fun _ -> selectedFile.ForceNotify())
        |> ignore

        selectedFileWhenActive <-
            let observable = selectedFile |> Observable.filter (fun _ -> isActive.Value)
            observable.ToReadOnlyReactiveProperty(mode = ReactivePropertyMode.RaiseLatestValueOnSubscribe)

    member __.SearchString = searchString
    member __.SearchFromBaseDirectory = searchFromBaseDirectory
    member __.CanToggleSearchFromBaseDirectory = canToggleSearchFromBaseDirectory
    member __.IsActive = isActive
    member __.Files = files
    member __.Header = header
    member __.SelectedFile = selectedFile
    member __.SelectedFileWhenActive = selectedFileWhenActive
    member __.RefreshCommand = refreshCommand
    member __.ClearSearchStringCommand = clearSearchStringCommand
    member __.IsUpdating = isUpdating
    member __.FilterHasNoFeatures = filterHasNoFeatures
    member __.FilterHasFeatures = filterHasFeatures

type FileOperation =
    | AddRename of oldFile:FileInfo * newName:string
    | RemoveRename of oldNames:string list
    | AddDelete of FileInfo
    | RemoveDelete of string list

type UnderscoreHandling = Ignore = 0 | Replace = 1 | TrimSuffix = 2

type FileChange = Added | Removed

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
        new ReactiveProperty<_>(Unchecked.defaultof<DirectoryInfo>, ReactivePropertyMode.None)
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

    let mutable showSettings = new ReactiveProperty<_>(false)
    let mutable saveSettingsCommand = Unchecked.defaultof<ReactiveCommand>

    let originalFileName = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let originalFileNameSelectedText = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let newFileName = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let newFileNameSelectedText = new ReactiveProperty<_>("", ReactivePropertyMode.None)

    let treatParenthesizedPartAsNames = new ReactiveProperty<_>(true)
    let fixupNamesInMainPart = new ReactiveProperty<_>(true)
    let underscoreHandling = new ReactiveProperty<_>(UnderscoreHandling.TrimSuffix)
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
    let enableFeatureEditing = new ReactiveProperty<_>()
    let mutable removeFeatureInstanceRowCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable addFeatureInstanceRowCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable clearSelectedFeatureCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable expandAllFeaturesCommand = Unchecked.defaultof<ReactiveCommand>
    let mutable collapseAllFeaturesCommand = Unchecked.defaultof<ReactiveCommand>

    let resultingFilePath = new ReactiveProperty<_>("", ReactivePropertyMode.None)
    let mutable applyCommand = Unchecked.defaultof<ReactiveCommand<_, _>>

    let watcher = new FileSystemWatcher(EnableRaisingEvents = false, IncludeSubdirectories = true)

    let updateDirectoriesList baseDirectory prefixes filterByPrefixes =
        directories.Clear()

        Directory.GetDirectories baseDirectory
        |> Seq.map DirectoryInfo
        |> Seq.filter (fun di ->
            match filterByPrefixes, prefixes with
            | false, _ | _, "" -> true
            | _ -> prefixes |> Seq.exists (string >> di.Name.StartsWith))
        |> Seq.sortWith (fun x y -> Interop.StrCmpLogicalW(x.Name, y.Name))
        |> Seq.iter directories.Add

    let updateDestinationDirectories (prefixes: string) (currentFilePath: string) =
        let startsWithAny parts (s: string) =
            parts |> Seq.toList |> List.exists (string >> s.StartsWith)

        let currentFileDirectory = Path.GetDirectoryName currentFilePath

        destinationDirectories.Clear()

        let filter =
            if String.IsNullOrWhiteSpace prefixes
            then fun _ -> true
            else startsWithAny prefixes

        currentFileDirectory ::
        (Directory.GetDirectories this.BaseDirectory.Value
        |> Array.filter (Path.GetFileName >> filter)
        |> Array.sortWith (fun x y -> Interop.StrCmpLogicalW(x, y))
        |> Array.toList)
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
                    let vm = NameViewModel(trim name, false, false, true)
                    allNames.AddOrUpdate vm
                    vm)

            viewModel.IsSelected <- true

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

        if isInitial
        then
            let anyFeaturesSelected = featureInstances |> Seq.exists (fun vm -> vm.IsSelected)

            features
            |> Seq.iter (fun (vm: FeatureViewModel) ->
                if anyFeaturesSelected
                then vm.ResetExpanded()
                else vm.IsExpanded.Value <- true)

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
                    Path.Combine(this.SelectedDestinationDirectory.Value.FullName,
                                 this.NewFileName.Value + Path.GetExtension(selectedFile.Name)))

    let saveSettings baseDirectory =
        if Directory.Exists baseDirectory
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
        |> List.iter (fun name -> NameViewModel(name, false, false, false) |> allNames.AddOrUpdate)

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
            search.SearchFromBaseDirectory.Value <- true
            search.SearchString.Value <- text)

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
        RxApp.MainThreadScheduler <- DispatcherScheduler(Application.Current.Dispatcher)

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

        saveSettingsCommand <- ReactiveCommand.Create(fun () -> saveSettings this.BaseDirectory.Value)

        openCommand <-
            ReactiveCommand.Create(
                (fun (fi: FileInfo) -> Process.Start fi.FullName |> ignore),
                this.SelectedFile |> Observable.map (fun fi -> not <| isNull fi && fi.Exists))

        openFromSearchCommand <-
            ReactiveCommand.Create(fun (fi: FileInfo) ->
                if fi.Exists then Process.Start fi.FullName |> ignore)

        openExplorerCommand <-
            ReactiveCommand.Create(fun (fi: FileInfo) ->
                if not <| isNull fi
                then
                    fi.FullName
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
            ReactiveCommand.Create(fun (fi: FileInfo) ->
                if fi.Exists then Interop.showFileProperties fi.FullName |> ignore)

        deleteFileCommand <-
            ReactiveCommand.Create(fun (fi: FileInfo) ->
                if fi.Exists
                then
                    MessageBox.Show(sprintf "Delete file %s?" fi.FullName,
                                        "Delete File",
                                        MessageBoxButton.OKCancel,
                                        MessageBoxImage.Question)
                    |> function
                        | MessageBoxResult.OK ->
                            try
                                File.Delete fi.FullName
                                searchCommands.OnNext (Refresh [ fi ])

                                None
                            with _ -> Some [ AddDelete fi ]
                        | _ -> None
                else None)

        addReplacementCommand <-
            ReactiveCommand.Create(
                (fun () ->
                    { ToReplace = this.ToReplaceToAdd.Value; ReplaceWith = this.ReplaceWithToAdd.Value }
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
                            FeatureInstanceViewModel(feature.Feature,
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
                        File.Move(oldFile.FullName, newName)

                        [ oldFile; FileInfo newName ] |> Refresh |> searchCommands.OnNext

                        None
                    with _ -> [ AddRename (oldFile, newName) ] |> Some),
                [
                    this.SelectedFile |> Observable.map (fun fi -> not <| isNull fi && fi.Exists)

                    this.ResultingFilePath
                    |> Observable.map (fun path ->
                        not <| isNull path
                        && (not <| File.Exists path
                            || this.SelectedFile.Value.FullName <> path
                                && String.Compare(this.SelectedFile.Value.FullName, path, true) = 0))
                ]
                |> Observable.combineLatestSeq
                |> Observable.map (Seq.toList >> List.forall id))

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
                        toRename.[oldFile.FullName] <- { OriginalFile = oldFile; NewFilePath = newName }
                    | RemoveRename oldNames -> oldNames |> Seq.iter (toRename.Remove >> ignore)
                    | AddDelete file -> toDelete.[file.FullName] <- file
                    | RemoveDelete fileNames -> fileNames |> Seq.iter (toDelete.Remove >> ignore)

                    { RenamedFiles = toRename; DeletedFiles = toDelete }))
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

                    if File.Exists oldFile.FullName
                    then
                        try
                            File.Move(oldFile.FullName, newName)
                            Some (oldFile, Some (FileInfo newName))
                        with _ -> None
                    else Some (oldFile, None))
                |> Seq.toList
                |> List.unzip

            let deletedFiles =
                fileChanges.DeletedFiles.Values
                |> Seq.choose (fun file ->
                    try
                        File.Delete file.FullName
                        Some file
                    with _ -> None)
                |> Seq.toList

            [
                renamedFiles |> List.map (fun fi -> fi.FullName) |> RemoveRename
                deletedFiles |> List.map (fun fi -> fi.FullName) |> RemoveDelete
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
        |> Observable.map (fun dir -> Directories (Option.ofObj dir, this.BaseDirectory.Value))
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
            |> Observable.filter Directory.Exists

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
                Directory.EnumerateFiles(directory,"*.*", SearchOption.AllDirectories)
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
            |> Observable.map (fun changes ->
                changes
                |> List.collect (fun (filePath, change) ->
                    Path.GetFileNameWithoutExtension filePath
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
                match allNames.Items |> Seq.tryFind (fun vm -> vm.Name.Value = name) with
                | Some vm -> vm.Count.Value <- count
                | None -> ()))
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
        |> Observable.map (fun fi -> fi.FullName)
        |> Observable.combineLatest
            (this.DestinationDirectoryPrefixes
             |> Observable.throttle (TimeSpan.FromMilliseconds 500.)
             |> Observable.observeOn RxApp.MainThreadScheduler)
        |> Observable.subscribe (uncurry updateDestinationDirectories)
        |> ignore

        existingSelectedFile
        |> Observable.subscribe (fun fi ->
            allNames.Items
            |> Seq.filter (fun vm -> vm.IsNewlyDetected.Value)
            |> Seq.toList
            |> List.iter (allNames.Remove >> ignore)

            this.OriginalFileName.Value <-
                string fi.Name |> Path.GetFileNameWithoutExtension

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
                change.PropertyName = nameof <@ any<FeatureInstanceViewModel>.IsSelected @>)
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
            .Filter(nameFilter)
            .Sort(NameViewModelComparer())
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

    member __.Shutdown () = saveSettings __.BaseDirectory.Value

    member __.HasTouchInput = hasTouchInput

    member __.BaseDirectory: ReactiveProperty<string> = baseDirectory
    member __.FilterBySourceDirectoryPrefixes: ReactiveProperty<_> = filterBySourceDirectoryPrefixes
    member __.SourceDirectoryPrefixes: ReactiveProperty<_> = sourceDirectoryPrefixes
    member __.RefreshDirectoriesCommand = refreshDirectoriesCommand

    member __.SelectedDirectory: ReactiveProperty<DirectoryInfo> = selectedDirectory

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

    member __.ShowSettings = showSettings
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

    member __.EnableFeatureEditing: ReactiveProperty<bool> = enableFeatureEditing

    member __.ResultingFilePath: ReactiveProperty<string> = resultingFilePath

    member __.ApplyCommand = applyCommand