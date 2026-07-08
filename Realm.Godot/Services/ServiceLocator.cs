using System;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceLocator
{
	private static IServiceProvider? _provider;

	public static void Initialize(IServiceProvider provider)
	{
		_provider = provider;
	}

	public static T Get<T>() where T : class
	{
		if (_provider == null)
			throw new InvalidOperationException("ServiceLocator has not been initialized yet.");

		return _provider.GetRequiredService<T>();
	}
}
