﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.BuildEngine;
using Microsoft.Build.Evaluation;
using NuGet;
using Project = Microsoft.Build.Evaluation.Project;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            var cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cache");
            var orchardDirectory = @"E:\Marcin\Documents\Praca\Projekty\SymbolSource.Orchard\pkg";
            var inputDirectory = @"E:\Marcin\Documents\Praca\Projekty\SymbolSource.Orchard\pkgsrc";
            var outputDirectory = @"E:\Marcin\Documents\Praca\Projekty\SymbolSource.Orchard\pkgbin";

            var cacheFileSystem = new PhysicalFileSystem(cacheDirectory);
            var cachePackageResolver = new DefaultPackagePathResolver(cacheFileSystem, false);

            var orchardRepository = new AggregateRepository(new IPackageRepository[]
                {
                    new LocalPackageRepository(orchardDirectory),
                    new DataServicePackageRepository(new Uri("http://nuget.org/api/v2")), 
                });

            var orchardManager = new PackageManager(orchardRepository, cachePackageResolver, cacheFileSystem);

            var inputRepository = new LocalPackageRepository(inputDirectory);
            var inputManager = new PackageManager(inputRepository, cachePackageResolver, cacheFileSystem);

            var references = new[] { "Orchard.Core", "Orchard.Framework", "Orchard.External" };

            foreach (var reference in references)
                orchardManager.InstallPackage(orchardRepository.FindPackage(reference), false, false);

            foreach (var inputPackage in inputRepository.GetPackages().Where(p => p.Id.StartsWith("Orchard.Module.")))
            {
                Do(inputRepository, inputManager, inputPackage, cacheDirectory, references, outputDirectory);
            }
        }

        private static void Do(LocalPackageRepository inputRepository, PackageManager inputManager, IPackage inputPackage, string cacheDirectory, IEnumerable<string> references, string outputDirectory)
        {
            var solution = new ProjectCollection();
            var logger = new ConsoleLogger();

            inputManager.InstallPackage(inputPackage, false, false);
            var packageDirectory = Path.Combine(cacheDirectory, inputPackage.Id);
            var moduleDirectory = Directory.GetDirectories(Path.Combine(packageDirectory, "Content", "Modules"))[0];
            var moduleName = Path.GetFileName(moduleDirectory);
            var project = solution.LoadProject(Path.Combine(moduleDirectory, moduleName + ".csproj"));

            var candidateDirectories = references
                   .Select(r => Path.Combine(cacheDirectory, r))
                   .Concat(Directory.EnumerateDirectories(cacheDirectory).Where(d => !Path.GetFileName(d).StartsWith("Orchard.")))
                   .Join(new[] { "net40", "net20", "net", "" }, l => true, r => true, (l, r) => Path.Combine(l, "lib", r))
                   .Where(Directory.Exists)
                   .ToList();

            foreach (var item in GetReferences(project).ToList())
            {
                var referenceName = GetReferenceName(item);
                var referenceDirectory = candidateDirectories.FirstOrDefault(d => File.Exists(Path.Combine(d, referenceName + ".dll")));
                
                if (referenceDirectory != null)
                    ReplaceReference(project, item, referenceName, referenceDirectory);
            }

            foreach (var item in GetModuleReferences(project).ToList())
            {
                var referencedModuleName = item.GetMetadataValue("Name");
                var referencedPackageId = "Orchard.Module." + item.GetMetadataValue("Name");

                Do(inputRepository, inputManager, inputRepository.FindPackage(referencedPackageId), cacheDirectory, references, outputDirectory);
                ReplaceReference(project, item, referencedModuleName, Path.Combine(cacheDirectory, referencedPackageId, "Content", "Modules", referencedModuleName, "bin"));
            }
            
            if (!File.Exists(Path.Combine(moduleDirectory, "bin", moduleName + ".dll")))
                if (!project.Build(logger))
                    throw new Exception("Failed to build");

            var rules = new[]
                {
                    @"bin\" + moduleName + ".dll",
                    @"Module.txt",
                    @"Placement.info",
                    @"Web.config",
                    @"Content\**",
                    @"Scripts\**",
                    @"Recipes\**",
                    @"Styles\**",
                    @"Views\**"
                };

            var manifest = Manifest.Create(inputPackage);
            manifest.Files = rules.Select(r => new ManifestFile { Source = @"Content\Modules\" + moduleName + @"\**\" + r, Target = @"Content\Modules\" + moduleName }).ToList();

            var b = new PackageBuilder();
            b.Populate(manifest.Metadata);
            b.PopulateFiles(packageDirectory, manifest.Files);

            Directory.CreateDirectory(outputDirectory);
            using (var outputPackageFile = File.Create(Path.Combine(outputDirectory, manifest.Metadata.Id + "." + manifest.Metadata.Version + ".nupkg")))
                b.Save(outputPackageFile);
        }

        private static string GetReferenceName(ProjectItem item)
        {
            if (item.ItemType == "ProjectReference")
                return Path.GetFileNameWithoutExtension(item.EvaluatedInclude);
            
            if (item.ItemType == "Reference")
                return new AssemblyName(item.EvaluatedInclude).Name;
            
            throw new NotSupportedException(); 
        }

        private static IEnumerable<ProjectItem> GetReferences(Project project)
        {
            return project.Items.Where(i => i.ItemType == "ProjectReference" || i.ItemType == "Reference");
        }

        private static IEnumerable<ProjectItem> GetModuleReferences(Project project)
        {
            return project.Items
                .Where(i => i.ItemType == "ProjectReference")
                .Where(i => i.EvaluatedInclude.StartsWith(@"..\") && !i.EvaluatedInclude.StartsWith(@"..\..\"));
        }

        private static void ReplaceReference(Project project, ProjectItem item, string reference, string path)
        {
            project.RemoveItem(item);
            project.AddItem("Reference", reference, new[] { new KeyValuePair<string, string>("HintPath", Path.Combine(path, reference + ".dll")) });
        }

        private static void Latest()
        {
            var orchard = new DataServicePackageRepository(new Uri("http://packages.orchardproject.net/FeedService.svc"));
            var nuget = new DataServicePackageRepository(new Uri("https://nuget.org/api/v2"));
            var symbolsource =
                new DataServicePackageRepository(new Uri("http://nuget.gw.symbolsource.org/Public/Orchard/FeedService.mvc"));
            var cache = Directory.GetCurrentDirectory();


            var solution = new ProjectCollection();
            var logger = new ConsoleLogger();

            foreach (var d in Directory.EnumerateDirectories(Path.Combine(cache, "aa", "Content", "Modules")))
            {
                var project = solution.LoadProject(Path.Combine(d, Path.GetFileName(d) + ".csproj"));
                project.RemoveItem(
                    project.Items.Where(i => i.ItemType == "ProjectReference" && i.GetMetadataValue("Name") == "Orchard.Core").
                        Single());
                project.RemoveItem(
                    project.Items.Where(
                        i => i.ItemType == "ProjectReference" && i.GetMetadataValue("Name") == "Orchard.Framework").Single());
                project.AddItem("Reference", "Orchard.Core",
                                new[]
                                    {
                                        new KeyValuePair<string, string>("HintPath",
                                                                         @"C:\Users\marcin.mikolajczak\Downloads\Temp\Orchard.Core.dll")
                                    });
                project.AddItem("Reference", "Orchard.Framework",
                                new[]
                                    {
                                        new KeyValuePair<string, string>("HintPath",
                                                                         @"C:\Users\marcin.mikolajczak\Downloads\Temp\Orchard.Framework.dll")
                                    });
            }

            foreach (var project in solution.LoadedProjects)
                project.Build(logger);
        }
    }
}
