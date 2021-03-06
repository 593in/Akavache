﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Splat;
using System.Reactive;
using System.Reactive.Linq;

#if ANDROID
using Android.App;
using Android.Net;
using Android.Telephony;
#endif

#if UIKIT
using MonoTouch.SystemConfiguration;
using MonoTouch.CoreTelephony;
#endif

#if WINRT
using Windows.Networking.Connectivity;
#endif

namespace Akavache.Http
{
    public class Registrations : IWantsToRegisterStuff
    {
        public void Register(IMutableDependencyResolver resolver)
        {
            var background = new Lazy<IHttpScheduler>(() =>
            {
                var ret = new CachingHttpScheduler(new HttpScheduler((int)Priorities.Background, 1));

                // NB: This is an end-run around not having ReactiveUI as a reference
                // but it being real damn useful at this point for detecting suspension
                // Akavache.Mobile sets up this Observable in its initializer
                var shouldPersistState = resolver.GetService<IObservable<IDisposable>>("ShouldPersistState")
                    ?? Observable.Never<IDisposable>(); 

                if (shouldPersistState != null)
                {
                    shouldPersistState.Subscribe(_ => ret.CancelAll());
                }
                return ret;
            });
            resolver.Register(() => background.Value, typeof(IHttpScheduler), "Background");

            var userInitiated = new Lazy<IHttpScheduler>(() =>
            {
                var ret = new CachingHttpScheduler(new HttpScheduler((int)Priorities.UserInitiated, 3));

                var shouldPersistState = resolver.GetService<IObservable<IDisposable>>("ShouldPersistState")
                    ?? Observable.Never<IDisposable>();

                if (shouldPersistState != null)
                {
                    shouldPersistState.Subscribe(_ => ret.CancelAll());
                }
                return ret;
            });
            resolver.Register(() => userInitiated.Value, typeof(IHttpScheduler), "UserInitiated");

            var speculative = new Lazy<IHttpScheduler>(() =>
            {
                var ret = new CachingHttpScheduler(new HttpScheduler((int)Priorities.Speculative, 0));
                ret.ResetLimit(GetDataLimit());

                var shouldPersistState = resolver.GetService<IObservable<IDisposable>>("ShouldPersistState")
                    ?? Observable.Never<IDisposable>();
                var isUnpausing = resolver.GetService<IObservable<Unit>>("IsUnpausing") 
                    ?? Observable.Never<Unit>();

                if (shouldPersistState != null)
                {
                    shouldPersistState.Subscribe(_ => ret.CancelAll());
                    isUnpausing.Subscribe(_ => ret.ResetLimit(GetDataLimit()));
                }

                return ret;
            });
            resolver.Register(() => speculative.Value, typeof(ISpeculativeHttpScheduler), "Speculative");
        }

#if PORTABLE
        static long GetDataLimit()
        {
            return 5 * 1048576;
        }
#endif

#if NET45 || APPKIT
        static long GetDataLimit()
        {
            return 10 * 1048576;
        }
#endif

#if UIKIT
        static long GetDataLimit()
        {
            var nm = new NetworkReachability("google.com");
            var flags = default(NetworkReachabilityFlags);

            if (!nm.TryGetFlags(out flags)) {
                return 512 * 1024;
            }

            if (!flags.HasFlag(NetworkReachabilityFlags.IsWWAN)) {
                return 10 * 1048576;
            }

            var netInfo = new CTTelephonyNetworkInfo();
            var r = netInfo.CurrentRadioAccessTechnology;
            if (r == CTRadioAccessTechnology.CDMA1x || r == CTRadioAccessTechnology.Edge) 
            {
                return 512 * 1024;
            }

            if (new[] { CTRadioAccessTechnology.WCDMA, CTRadioAccessTechnology.HSDPA, 
                CTRadioAccessTechnology.HSUPA, CTRadioAccessTechnology.EHRPD }.Contains(r)) 
            {
                return 2 * 1048576;
            }

            if (r == CTRadioAccessTechnology.LTE) 
            {
                return 5 * 1048576;
            }

            return 512 * 1024;
        }
#endif

#if ANDROID
        static long GetDataLimit()
        {
            var cm = Application.Context.GetSystemService(Application.ConnectivityService) as ConnectivityManager;
            if (cm == null || cm.ActiveNetworkInfo == null || cm.ActiveNetworkInfo.IsRoaming) 
            {
                return 512 * 1024;
            }

            switch (cm.ActiveNetworkInfo.Type) 
            {
                case ConnectivityType.Mobile:
                case ConnectivityType.MobileDun:
                case ConnectivityType.MobileHipri:
                    var tm = Application.Context.GetSystemService(Application.TelephonyService) as TelephonyManager;
                    if (tm == null) 
                    {
                        return 512 * 1024;
                    }
                    switch (tm.NetworkType) 
                    {
                        case NetworkType.Hsdpa:
                        case NetworkType.Hspap:
                        case NetworkType.Hspa:
                        case NetworkType.Cdma:
                            return 2 * 1048576;
                        case NetworkType.Lte:
                            return 5 * 1048576;
                        default:
                            return 512 * 1024;
                    }
                case ConnectivityType.Bluetooth:
                case ConnectivityType.Ethernet:
                case ConnectivityType.Wifi:
                case ConnectivityType.Wimax:
                    return 5 * 1048576;

                default:
                    return 512 * 1024;
            }
        }
#endif

#if WINRT
        static long GetDataLimit()
        {
            var ci = NetworkInformation.GetConnectionProfiles().FirstOrDefault();
            if (ci == null || ci.GetConnectionCost().NetworkCostType != NetworkCostType.Unrestricted)
            {
                return 512 * 1024;
            }

            return 5 * 1048576;
        }
#endif
    }
}