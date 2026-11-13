using System.Runtime.CompilerServices;

// Card/clash rules live in this assembly; let the test assemblies exercise internal helpers.
[assembly: InternalsVisibleTo("DeckArpg.Tests.EditMode")]
[assembly: InternalsVisibleTo("DeckArpg.Tests.PlayMode")]
