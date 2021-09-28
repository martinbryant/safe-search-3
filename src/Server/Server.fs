module Server

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Hosting
open Saturn

open Shared
open Crime

type Foo = { Name : string }

let searchApi (context:HttpContext) =
    let config = context.GetService<IConfiguration>()
    let logger = context.GetService<ILogger<ISearchApi>>()
    {
        FreeText = fun request -> async {
            logger.LogInformation $"""Searching for '{request.Text}' on index '{config.["searchName"]}'"""
            let results = Search.freeTextSearch request.Text request.Filters config.["searchName"] config.["searchKey"]
            return results
        }
        ByLocation = fun request -> async {
            logger.LogInformation $"""Searching for '{request.Postcode}' on index '{config.["searchName"]}'"""
            let! geoLookupResult = GeoLookup.tryGetGeo config.["storageConnectionString"] request.Postcode |> Async.AwaitTask
            return
                match geoLookupResult with
                | Some geo ->
                    logger.LogInformation $"{request.Postcode} => {(geo.Long, geo.Lat)}."
                    let results = Search.locationSearch (geo.Long, geo.Lat) request.Filters config.["searchName"] config.["searchKey"]
                    Ok results
                | None ->
                    Error "Invalid postcode"
        }
        GetCrimes = fun geo -> async {
            let! reports = getCrimesNearPosition geo
            let crimes =
                reports
                |> Array.countBy(fun r -> r.Category)
                |> Array.sortByDescending snd
                |> Array.map(fun (k, c) -> { Crime = k; Incidents = c })
            return crimes
        }
        GetSuggestions = fun searchedTerm -> async {
            let results =
                Search.suggestionsSearch searchedTerm config.["searchName"] config.["searchKey"]
                |> Seq.map (fun suggestion -> suggestion.ToLower())
                |> Seq.toArray
            return { Suggestions = results }
        }
    }

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withErrorHandler (fun ex _ -> printfn "%O" ex; Ignore)
    |> Remoting.fromContext searchApi
    |> Remoting.buildHttpHandler

let app =
    application {
        url "http://0.0.0.0:8085"
        logging (fun logging -> logging.AddConsole() |> ignore)
        webhost_config (fun config ->
            config.ConfigureAppConfiguration(fun c -> c.AddUserSecrets<Foo>() |> ignore)
        )
        memory_cache
        use_static "public"
        use_gzip
        use_router webApp
    }

run app
