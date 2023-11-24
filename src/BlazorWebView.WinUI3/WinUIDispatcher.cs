// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components.WebView.WinUI
{
    internal sealed class WinUIDispatcher : Dispatcher
    {
        private readonly DispatcherQueue _dispatcherQueue;

        public WinUIDispatcher(DispatcherQueue windowsDispatcher)
        {
            _dispatcherQueue = windowsDispatcher ?? throw new ArgumentNullException(nameof(windowsDispatcher));
        }

        public override bool CheckAccess()  => _dispatcherQueue.HasThreadAccess;

        public override async Task InvokeAsync(Action workItem)
        {
            try
            {
                if (_dispatcherQueue.HasThreadAccess)
                {
                    workItem();
                }
                else
                {
                    await _dispatcherQueue.EnqueueAsync(workItem);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public override async Task InvokeAsync(Func<Task> workItem)
        {
            try
            {
                if (_dispatcherQueue.HasThreadAccess)
                {
                    await workItem();
                }
                else
                {
                    await _dispatcherQueue.EnqueueAsync(workItem);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public override async Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            try
            {
                if (_dispatcherQueue.HasThreadAccess)
                {
                    return workItem();
                }
                else
                {
                    return await _dispatcherQueue.EnqueueAsync(workItem);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            try
            {
                if (_dispatcherQueue.HasThreadAccess)
                {
                    return await workItem();
                }
                else
                {
                    return await _dispatcherQueue.EnqueueAsync(workItem);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
