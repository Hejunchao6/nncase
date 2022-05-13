﻿// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using Autofac;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nncase.CodeGen;
using Nncase.Compiler;
using Nncase.IR;
using Nncase.Transform;

namespace Nncase.Cli.Commands
{
    /// <summary>
    /// Compile command.
    /// </summary>
    public class Compile : Command
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Compile"/> class.
        /// </summary>
        public Compile()
            : base("compile")
        {
            AddArgument(new Argument("input-file"));
            AddArgument(new Argument("output-file"));
            AddOption(new Option<string>(new[] { "-t", "--target" }, "target architecture, e.g. cpu, k210") { IsRequired = true });
            AddOption(new Option<string>(new[] { "-i", "--input-format" }, "input format, e.g. tflite") { IsRequired = true });
            AddOption(new Option<int>("--dump-level", () => 0, "dump ir to .il, default is 0") { IsRequired = false });
            AddOption(new Option<string>("--dump-dir", () => ".", "dump to directory, default is .") { IsRequired = false });

            Handler = CommandHandler.Create<ICompileOptions, IHost>(Run);
        }

        internal void ConfigureServices(IHost host)
        {
            var provider = host.Services.GetRequiredService<ICompilerServicesProvider>();
            CompilerServices.Configure(provider);
        }

        private void Run(ICompileOptions options, IHost host)
        {
            ConfigureServices(host);
            var module = ImportModule(File.OpenRead(options.InputFile), options);
            BuildKModel(module, options);
        }

        private void BuildKModel(IRModule module, CompileOptions options)
        {
            var target = CompilerServices.GetTarget(options.Target);
            var modelBuilder = new ModelBuilder(target);
            using (var output = new FileStream(options.OutputFile, FileMode.Create, FileAccess.ReadWrite))
            {
                var model = modelBuilder.Build(module);
                model.Serialize(output);
            }
        }

        private IRModule ImportModule(Stream content, CompileOptions options)
        {
            Console.WriteLine($"Target: {options.Target}");
            var module = ImportModel(content, options);
            DumpModule(module, options, "ir_import");
            if (!CompilerServices.InferenceType(module.Entry))
            {
                InferShape(module, options);
            }

            DumpModule(module, options, "ir_infertype");
            Console.WriteLine("ImportModule successful!");
            return module;
        }

        private void InferShape(IRModule module, CompileOptions options)
        {
            Console.WriteLine("Infer Shape...");
            var pmgr = new PassManager(module, new RunPassOptions(null, options.DumpLevel, options.DumpDir));
            //var constFold = new ShapeInferPass();
            //pmgr.Add(constFold);
            //pmgr.Run();
        }

        private IRModule ImportModel(Stream content, CompileOptions options) =>
          options.InputFormat switch
          {
              "tflite" => Importers.ImportTFLite(content),
              "onnx" => Importers.ImportOnnx(content),
              _ => throw new NotImplementedException($"Not Implement {options.InputFormat} Impoter!"),
          };

        private void DumpModule(IRModule module, CompileOptions options, string prefix)
        {
            var dumpPath = Path.Combine(options.DumpDir, "dump");
            CompilerServices.DumpIR(module.Entry!, prefix, dumpPath);
        }
    }
}
