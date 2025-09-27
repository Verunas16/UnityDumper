# UnityDumper

A console tool for analyzing Unity projects and extracting information about unused scripts and scene hierarchies.

## Features

- *Unused Script Detection*: Identifies C# scripts that are not referenced in Unity scenes
- *Scene Hierarchy Export*: Dumps the hierarchy structure of all Unity scenes
- *Parallel Processing*: Runs script analysis and scene dumping concurrently for better performance

## Usage


UnityDumper.exe <InputFolder> <OutputFolder>


- InputFolder: Path to your Unity project folder
- OutputFolder: Path where the analysis results will be saved

## Output Files

- UnusedScripts.csv: List of unused C# scripts with their paths and GUIDs
- *.unity.dump: Text files containing the hierarchy structure of each scene

## Requirements

- .NET 9.0
- Microsoft.CodeAnalysis package

## Building

bash
dotnet build


The compiled executable will be available in the builds folder.
