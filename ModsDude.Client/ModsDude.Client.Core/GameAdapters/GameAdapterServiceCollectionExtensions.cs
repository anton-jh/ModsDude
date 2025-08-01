﻿using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace ModsDude.Client.Core.GameAdapters;
public static class GameAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGameAdapters(this IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(x => !x.IsAbstract && x.IsAssignableTo(typeof(IGameAdapter)));

        foreach (var type in types)
        {
            services.AddSingleton(typeof(IGameAdapter), type);
        }

        services.AddSingleton<IGameAdapterIndex, GameAdapterIndex>();

        return services;
    }
}
