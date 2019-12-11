﻿
#if INTERACTIVE
#r @"../../packages/fsharp.data/lib/net45/FSharp.Data.dll"
#r @"../../packages/NUnit/lib/netstandard2.0/nunit.framework.dll"
#r @"../../packages/fsunit/lib/netstandard2.0/FsUnit.NUnit.dll"
#r @"../../packages/Suave/lib/netstandard2.0\Suave.dll"
#load @"../FsHttp/bin/Debug/netstandard2.0/FsHttp.fsx"
#r @"../FsHttp.NUnit/bin/Debug/netstandard2.0/FsHttp.NUnit.dll"
#load @"./Server.fs"
#else
module ``Integration tests for FsHttp``
#endif

open FsUnit
open FsHttp
open FsHttp.DslCE
open FsHttp.Testing
open NUnit.Framework
open Server
open System
open Suave
open Suave.Cookie
open Suave.ServerErrors
open Suave.Operators
open Suave.Filters
open Suave.Utils.Collections
open Suave.Successful

[<AutoOpen>]
module Helper =

    let keyNotFoundString = "KEY_NOT_FOUND"
    let query key (r: HttpRequest) = defaultArg (Option.ofChoice (r.query ^^ key)) keyNotFoundString
    let header key (r: HttpRequest) = defaultArg (Option.ofChoice (r.header key)) keyNotFoundString
    let form key (r: HttpRequest) = defaultArg (Option.ofChoice (r.form ^^ key)) keyNotFoundString
    let text (r: HttpRequest) = r.rawForm |> System.Text.Encoding.UTF8.GetString


[<TestCase>]
let ``Synchronous GET call is invoked immediately``() =
    use server = GET >=> request (fun r -> r.rawQuery |> OK) |> serve

    http { GET (url @"?test=Hallo") }
    |> toText
    |> should equal "test=Hallo"

[<TestCase>]
let ``Split URL are interpreted correctly``() =
    use server = GET >=> request (fun r -> r.rawQuery |> OK) |> serve

    http { GET (url @"
                    ?test=Hallo
                    &test2=Welt")
    }
    |> toText
    |> should equal "test=Hallo&test2=Welt"

[<TestCase>]
let ``Smoke test for a header``() =
    use server = GET >=> request (header "accept-language" >> OK) |> serve

    let lang = "zh-Hans"
    
    http {
        GET (url @"")
        AcceptLanguage lang
    }
    |> toText
    |> should equal lang

[<TestCase>]
let ``ContentType override``() =
    use server = POST >=> request (header "content-type" >> OK) |> serve

    let contentType = "application/xxx"

    http {
        POST (url @"")
        body
        ContentType contentType
        text "hello world"
    }
    |> toText
    |> should contain contentType

[<TestCase>]
let ``Multiline urls``() =
    use server = 
        GET
        >=> request (fun r -> (query "q1" r) + "_" + (query "q2" r) |> OK)
        |> serve

    http {
        GET (url @"
                    ?q1=Query1
                    &q2=Query2")
    }
    |> toText
    |> should equal "Query1_Query2"

[<TestCase>]
let ``Comments in urls are discarded``() =
    use server =
        GET 
        >=> request (fun r -> (query "q1" r) + "_" + (query "q2" r) + "_" + (query "q3" r) |> OK)
        |> serve

    http {
        GET (url @"
                    ?q1=Query1
                    //&q2=Query2
                    &q3=Query3")
    }
    |> toText
    |> should equal ("Query1_" + keyNotFoundString + "_Query3")

[<TestCase>]
let ``POST string data``() =
    use server =
        POST 
        >=> request (text >> OK)
        |> serve

    let data = "hello world"

    http {
        POST (url @"")
        body
        text data
    }
    |> toText
    |> should equal data

[<TestCase>]
let ``POST binary data``() =
    use server =
        POST 
        >=> request (fun r -> r.rawForm |> Suave.Successful.ok)
        |> serve

    let data = [| 12uy; 22uy; 99uy |]

    http {
        POST (url @"")
        body
        binary data
    }
    |> toBytes
    |> should equal data

[<TestCase>]
let ``POST Form url encoded data``() =
    use server =
        POST 
        >=> request (fun r -> (form "q1" r) + "_" + (form "q2" r) |> OK) 
        |> serve

    http {
        POST (url @"")
        body
        formUrlEncoded [
            "q1","Query1"
            "q2","Query2"
        ]
    }
    |> toText
    |> should equal ("Query1_Query2")

[<TestCase>]
let ``POST Multipart form data``() =
    use server =
        POST 
        >=> request (fun r -> r.files.Length.ToString() |> OK)
        |> serve

    http {
        POST (url @"")
        body
        multipart
        filePart "c:\\temp\\test.txt"
        filePart "c:\\temp\\test.txt"
    }
    |> toText
    |> should equal ("2")

// TODO: Post single file

// TODO: POST stream
// TODO: POST multipart

[<TestCase>]
let ``Expect status code``() =
    use server = GET >=> BAD_GATEWAY "" |> serve

    http { GET (url @"") }
    |> statusCodeShouldBe System.Net.HttpStatusCode.BadGateway
    |> ignore

    Assert.Throws<AssertionException>(fun() ->
        http { GET (url @"") }
        |> statusCodeShouldBe System.Net.HttpStatusCode.Ambiguous
        |> ignore
    )
    |> ignore

[<TestCase>]
let ``Specify content type explicitly``() =
    use server = POST >=> request (header "content-type" >> OK) |> serve

    let contentType = "application/whatever"
    
    http {
        POST (url @"")
        body
        ContentType contentType
    }
    |> toText
    |> should contain contentType

[<TestCase>]
let ``Cookies can be sent``() =
    use server =
        GET
        >=> request (fun r ->
            r.cookies
            |> Map.find "test"
            |> fun httpCookie -> httpCookie.value
            |> OK)
        |> serve

    http {
        GET (url @"")
        Cookie "test" "hello world"
    }
    |> toText
    |> should equal "hello world"

[<TestCase>]
let ``Custom HTTP method``() =
    use server =
        ``method`` (HttpMethod.parse "FLY")
        >=> request (fun r -> OK "flying")
        |> serve

    http {
        Request "FLY" (url @"")
    }
    |> toText
    |> should equal "flying"


[<TestCase>]
let ``Custom Headers``() =
    let customHeaderKey = "X-Custom-Value"

    use server =
        GET
        >=> request (fun r ->
            r.header customHeaderKey
            |> function | Choice1Of2 v -> v | Choice2Of2 e -> failwithf "Failed %s" e
            |> OK)
        |> serve

    http {
        GET (url @"")
        Header customHeaderKey "hello world"
    }
    |> toText
    |> should equal "hello world"


// [<TestCase>]
// let ``Http reauest message can be modified``() =
//     use server = GET >=> request (header "accept-language" >> OK) |> serve
    
//     let lang = "fr"
//     http {
//         GET (url @"")
//         transformHttpRequestMessage (fun httpRequestMessage ->
//             httpRequestMessage
//         )
//     }
//     |> toText
//     |> should equal lang

// TODO: Timeout
// TODO: ToFormattedText
// TODO: transformHttpRequestMessage
// TODO: transformHttpClient
// TODO: Cookie tests (test the overloads)
