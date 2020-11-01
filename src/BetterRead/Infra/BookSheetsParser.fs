﻿module BetterRead.Infra.BookSheetsParser

open System
open BetterRead.Configuration
open BetterRead.Domain.Book
open HtmlAgilityPack
open Fizzler.Systems.HtmlAgilityPack

let private getAttributeValue (node:HtmlNode) (attr:string) =
    node.GetAttributeValue(attr, String.Empty)

let private getHtmlNodeAsync (htmlWeb:HtmlWeb) bookId pageId =
    async {
        let url = BookUrls.bookPage bookId pageId
        let! document = htmlWeb.LoadFromWebAsync url |> Async.AwaitTask
        return (pageId, document.DocumentNode)
    }

let private getPageFactory htmlWeb bookId =
    getHtmlNodeAsync htmlWeb bookId

let private getPagesCount (node:HtmlNode) =
    node.QuerySelectorAll "div.navigation > a"
    |> Seq.choose (fun n ->
           match Int32.TryParse(n.InnerHtml) with
           | (true, x) -> Some x
           | _ -> None)
    |> Seq.max

let private getPageWithNodes (_, node:HtmlNode) =
    match node.QuerySelectorAll "div.MsoNormal" |> Seq.tryExactlyOne with
    | Some divNode -> Some divNode.ChildNodes
    | None -> None

let private validateAttrs (attrs:HtmlAttributeCollection) pattern =
    attrs |> Seq.exists (fun x -> x.Value = pattern || x.Value.Contains pattern)

let private parseNode (node:HtmlNode) =
    match node.Attributes with
    | attrs when validateAttrs attrs "take_h1" -> Header node.InnerText
    | attrs when validateAttrs attrs "MsoNormal" -> Paragraph node.InnerText
    | attrs when validateAttrs attrs "img/photo_books/" -> Image <| BookUrls.baseUrl + "/" + getAttributeValue node "src"
    | _ -> Unknown

let parse (htmlWeb:HtmlWeb) bookId =
    async {
        let concretePage = getPageFactory htmlWeb bookId
        let! (_, firstNode) = concretePage 1
        
        let pagesCount = getPagesCount firstNode
        let! pages =
            [1..pagesCount]
            |> Seq.map (fun idx -> concretePage idx)
            |> Async.Parallel
        
        let pageContents =
            pages
            |> Seq.choose getPageWithNodes
            |> Seq.collect (fun x -> x)
            |> Seq.map parseNode
            |> Seq.filter (function | Unknown -> false | _ -> true)
            |> Seq.toArray
        
        return pageContents
    }