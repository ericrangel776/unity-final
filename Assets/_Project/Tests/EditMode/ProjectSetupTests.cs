using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeckArpg.Tests.EditMode
{
    /// <summary>Guards the project configuration the rest of the codebase assumes.</summary>
    public class ProjectSetupTests
    {
        [Test]
        public void UniversalRenderPipeline_IsTheActivePipeline()
        {
            Assert.IsInstanceOf<UniversalRenderPipelineAsset>(GraphicsSettings.currentRenderPipeline);
        }

        [Test]
        public void InputSystem_HasProjectWideActionsAssigned()
        {
            Assert.IsNotNull(InputSystem.actions, "Project-wide input actions are not assigned.");
        }

        [Test]
        public void InputSystem_DefinesThePlayerActionMap()
        {
            Assert.IsNotNull(InputSystem.actions.FindActionMap("Player"), "Missing 'Player' action map.");
        }

        [TestCase("DeckArpg.Core")]
        [TestCase("DeckArpg.Gameplay")]
        public void GameAssembly_IsCompiledAndLoaded(string assemblyName)
        {
            var loaded = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name);
            CollectionAssert.Contains(loaded, assemblyName);
        }

        [Test]
        public void CoreAssembly_DoesNotDependOnHigherLayers()
        {
            var core = System.AppDomain.CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "DeckArpg.Core");
            var refs = core.GetReferencedAssemblies().Select(r => r.Name).ToArray();

            CollectionAssert.DoesNotContain(refs, "DeckArpg.Gameplay");
            CollectionAssert.DoesNotContain(refs, "DeckArpg.UI");
        }
    }
}
