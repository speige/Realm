using Templates.Ids;
using Templates.Definitions;
using System;

namespace Templates
{
    public static class TemplateExtensions
    {
        public static TChild Get<TChild>(this UniqueId<TChild> id) where TChild : struct, IBaseTemplate<TChild>
        {
            return TemplateRegistry.Get(id);
        }

        public static T Update<T>(this T template, Func<T, T> updateFunc) where T : struct, IBaseTemplate<T>
        {
            T updatedTemplate = updateFunc(template);

            if (!template.UniqueId.Equals(updatedTemplate.UniqueId))
            {
	            System.Diagnostics.Debug.Assert(false, $"The UniqueId of template type {typeof(T).Name} cannot be changed during an update. It is set internally during registration and must remain constant.");
	            return template;
            }

            TemplateRegistry.UpdateRegistration(updatedTemplate);
            return updatedTemplate;
        }

        public static T RegisterTemplate<T>(this string name, Func<T, T>? updateFunc = null) where T : struct, IBaseTemplate<T>
        {
            return TemplateRegistry.Register<T>(name, updateFunc);
        }

        public static T GetTemplate<T>(this string name) where T : struct, IBaseTemplate<T>
        {
            return TemplateRegistry.Get<T>(name);
        }
    }
}
