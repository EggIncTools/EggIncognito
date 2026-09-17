# Contributing

## Build and test

```sh
dotnet build
dotnet test
dotnet run --project EggIncognito
```

Codegen runs during `dotnet build`: controllers are generated from the route map, and the served stylesheet is compiled from the CSS source in pure C#. Generated files are overwritten every build; edit the sources, not the output.

## Submitting

- Branch off `main`.
- Open a pull request describing the change and why.
