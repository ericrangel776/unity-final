using DeckArpg.Core;
using NUnit.Framework;

namespace DeckArpg.Tests.EditMode
{
    public class InputBufferTests
    {
        [Test]
        public void Dequeue_ReturnsPressesInOrder()
        {
            var buffer = new InputBuffer<string>();
            buffer.Enqueue("a", 1.00f);
            buffer.Enqueue("b", 1.05f);

            Assert.IsTrue(buffer.TryDequeue(1.10f, out var first));
            Assert.IsTrue(buffer.TryDequeue(1.10f, out var second));
            Assert.AreEqual("a", first);
            Assert.AreEqual("b", second);
        }

        [Test]
        public void Press_WithinWindow_IsKept()
        {
            var buffer = new InputBuffer<int>(0.2f);
            buffer.Enqueue(1, 10f);
            Assert.IsTrue(buffer.TryDequeue(10.19f, out _));
        }

        [Test]
        public void Press_OlderThanWindow_IsDropped()
        {
            var buffer = new InputBuffer<int>(0.2f);
            buffer.Enqueue(1, 10f);
            Assert.IsFalse(buffer.TryDequeue(10.25f, out _));
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void ExpiredPress_IsSkipped_InFavourOfLiveOne()
        {
            var buffer = new InputBuffer<string>(0.2f);
            buffer.Enqueue("old", 10f);
            buffer.Enqueue("new", 10.3f);
            Assert.IsTrue(buffer.TryDequeue(10.35f, out var action));
            Assert.AreEqual("new", action);
        }

        [Test]
        public void Empty_ReturnsFalse()
        {
            Assert.IsFalse(new InputBuffer<int>().TryDequeue(0f, out _));
        }

        [Test]
        public void Full_DropsOldest()
        {
            var buffer = new InputBuffer<int>(1f, capacity: 2);
            buffer.Enqueue(1, 0f);
            buffer.Enqueue(2, 0f);
            buffer.Enqueue(3, 0f);
            buffer.TryDequeue(0f, out var first);
            Assert.AreEqual(2, first);
        }

        [Test]
        public void Peek_DoesNotConsume()
        {
            var buffer = new InputBuffer<int>();
            buffer.Enqueue(5, 0f);
            Assert.IsTrue(buffer.TryPeek(0.1f, out var peeked));
            Assert.AreEqual(5, peeked);
            Assert.AreEqual(1, buffer.Count);
        }
    }
}
