﻿namespace TeaDriven.Zokhion.ViewModels

open System
open System.Reactive.Linq

open Reactive.Bindings

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

    let toReadOnlyReactivePropertyWithMode (mode: ReactivePropertyMode) (observable: IObservable<_>) =
        observable.ToReadOnlyReactiveProperty(mode=mode)

    let maxString maxLength (s: string) = s.Substring(0, Math.Min(s.Length, maxLength))