using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Configer.Services
{
    public static class DependencyCheckerRegistry
    {
        private static readonly List<IDependencyChecker> _checkers = new();

        static DependencyCheckerRegistry()
        {
            // Discover and register all concrete types that implement IDependencyChecker
            try
            {
                var assemblies = new[] { Assembly.GetExecutingAssembly() };

                foreach (var asm in assemblies)
                {
                    var types = asm.GetTypes()
                        .Where(t => typeof(IDependencyChecker).IsAssignableFrom(t)
                                    && !t.IsInterface
                                    && !t.IsAbstract);

                    foreach (var t in types)
                    {
                        // Only add if not already registered (by type)
                        if (_checkers.Any(c => c.GetType() == t))
                            continue;

                        // Only instantiate types with a public parameterless ctor
                        var ctor = t.GetConstructor(Type.EmptyTypes);
                        if (ctor == null)
                            continue;

                        try
                        {
                            if (Activator.CreateInstance(t) is IDependencyChecker inst)
                            {
                                _checkers.Add(inst);
                            }
                        }
                        catch
                        {
                            // Ignore failures - don't let one bad checker stop discovery
                        }
                    }
                }
            }
            catch
            {
                // Swallow any reflection errors during discovery
            }
        }

        public static void Register(IDependencyChecker checker)
        {
            if (checker == null) throw new ArgumentNullException(nameof(checker));
            if (!_checkers.Any(c => c.GetType() == checker.GetType()))
                _checkers.Add(checker);
        }

        public static IEnumerable<IDependencyChecker> GetCheckers() => _checkers;
    }
}
