﻿// Copyright (c) Canaan Inc. All rights reserved.
// Licensed under the Apache license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Nncase.Diagnostics;
using Nncase.IR;
using Nncase.TIR;

namespace Nncase.Transform;

/// <summary>
/// Mutators addable.
/// </summary>
public interface IMutatorsAddable
{
    /// <summary>
    /// Add the muatator.
    /// </summary>
    /// <typeparam name="T">Muatator type.</typeparam>
    /// <param name="parameters">Muatator's constructor parameters.</param>
    /// <returns>Add result.</returns>
    PrimFuncPass.AddResult<T> Add<T>(params object[] parameters)
        where T : ExprMutator;

    /// <summary>
    /// Add the muatator.
    /// </summary>
    /// <param name="mutatorType">Muatator type.</param>
    /// <param name="parameters">Muatator's constructor parameters.</param>
    /// <returns>Add result.</returns>
    PrimFuncPass.AddResult<ExprMutator> Add(Type mutatorType, params object[] parameters);
}

/// <summary>
/// TIR Mutator Pass.
/// NOTE only apply on prim func
/// Because of we will mutate the expression multiple times, so use MutatorCreator create the new mutator.
/// </summary>
public class PrimFuncPass : Pass<PrimFunction>, IMutatorsAddable
{
    private readonly List<MutatorDescriptor> _mutatorDescriptors = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimFuncPass"/> class.
    /// </summary>
    public PrimFuncPass()
    {
    }

    /// <inheritdoc/>
    public AddResult<T> Add<T>(params object[] parameters)
        where T : ExprMutator
    {
        var descriptor = new MutatorDescriptor(ActivatorUtilities.CreateFactory(typeof(T), parameters.Select(x => x.GetType()).ToArray()), null, parameters);
        _mutatorDescriptors.Add(descriptor);
        return new(this, descriptor);
    }

    /// <inheritdoc/>
    public AddResult<ExprMutator> Add(Type mutatorType, params object[] parameters)
    {
        var descriptor = new MutatorDescriptor(ActivatorUtilities.CreateFactory(mutatorType, parameters.Select(x => x.GetType()).ToArray()), null, parameters);
        _mutatorDescriptors.Add(descriptor);
        return new(this, descriptor);
    }

    /// <inheritdoc/>
    protected override Task<PrimFunction> RunCoreAsync(PrimFunction input, RunPassContext context)
    {
        var post = input;
        int count = 0;
        bool isMutated = false;

        do
        {
            foreach (var descriptor in _mutatorDescriptors)
            {
                var mutator = descriptor.Activate(CompileSession);
                post = (PrimFunction)mutator.Visit(post);
                isMutated = mutator.IsMutated;

                if (isMutated)
                {
                    var typeInferSuccess = CompilerServices.InferenceType(post);
                    if (DumpScope.Current.IsEnabled(DumpFlags.PassIR))
                    {
                        DumpScope.Current.DumpIR(post, $"{count++}_{mutator.GetType().Name}");
                    }

                    Trace.Assert(typeInferSuccess);
                    break;
                }
            }

            if (!isMutated)
            {
                break;
            }
        }
        while (true);

        return Task.FromResult(post);
    }

    /// <inheritdoc/>
    protected override Task OnPassStartAsync(PrimFunction input, RunPassContext context) => Task.CompletedTask;

    /// <inheritdoc/>
    protected override Task OnPassEndAsync(PrimFunction post, RunPassContext context) => Task.CompletedTask;

    private protected override string? GetDumpRelativePass(PrimFunction input) => input.Name;

    /// <summary>
    /// Add muatator result.
    /// </summary>
    /// <typeparam name="T">Muatator type.</typeparam>
    public struct AddResult<T> : IMutatorsAddable
        where T : ExprMutator
    {
        private readonly PrimFuncPass _primFuncPass;

        internal AddResult(PrimFuncPass primFuncPass, MutatorDescriptor descriptor)
        {
            _primFuncPass = primFuncPass;
            Descriptor = descriptor;
        }

        /// <summary>
        /// Gets descriptor.
        /// </summary>
        internal MutatorDescriptor Descriptor { get; }

        /// <inheritdoc/>
        public AddResult<T1> Add<T1>(params object[] parameters)
            where T1 : ExprMutator => _primFuncPass.Add<T1>(parameters);

        /// <inheritdoc/>
        public AddResult<ExprMutator> Add(Type mutatorType, params object[] parameters)
            => _primFuncPass.Add(mutatorType, parameters);

        /// <summary>
        /// Configure descriptor.
        /// </summary>
        /// <param name="configureRule">Configure descriptor action.</param>
        /// <returns>This add result.</returns>
        public AddResult<T> Configure(Action<T> configureRule)
        {
            Descriptor.Configure = (Action<ExprMutator>)Delegate.Combine(Descriptor.Configure, configureRule);
            return this;
        }
    }

    internal sealed class MutatorDescriptor
    {
        public MutatorDescriptor(ObjectFactory factory, Action<ExprMutator>? configure, object[] arguments)
        {
            Factory = factory;
            Configure = configure;
            Arguments = arguments;
        }

        public ObjectFactory Factory { get; }

        public Action<ExprMutator>? Configure { get; set; }

        public object[] Arguments { get; }

        public ExprMutator Activate(CompileSession compileSession)
        {
            using var scope = new CompileSessionScope(compileSession);
            var mutator = (ExprMutator)Factory(compileSession, Arguments);
            Configure?.Invoke(mutator);
            return mutator;
        }
    }
}
