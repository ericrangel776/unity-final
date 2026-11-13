using System.Runtime.CompilerServices;

// MonoBehaviours (deck manager, combat controller) live here; let the test assemblies reach internals.
[assembly: InternalsVisibleTo("DeckArpg.Tests.EditMode")]
[assembly: InternalsVisibleTo("DeckArpg.Tests.PlayMode")]
