using System;
using System.Collections;
using System.Collections.Generic;

namespace FutufonAutoWorkerMscModLoader
{
    // One frame at a time; stopping disposes every nested iterator immediately.
    internal sealed class DemoStepper : IDisposable
    {
        private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();

        public DemoStepper(IEnumerator routine) { _stack.Push(routine); }

        public bool Tick()
        {
            while (_stack.Count > 0)
            {
                var routine = _stack.Peek();
                if (!routine.MoveNext())
                {
                    _stack.Pop();
                    DisposeRoutine(routine);
                    continue;
                }
                var nested = routine.Current as IEnumerator;
                if (nested == null) return true;
                _stack.Push(nested);
            }
            return false;
        }

        public void Dispose()
        {
            while (_stack.Count > 0) DisposeRoutine(_stack.Pop());
        }

        private static void DisposeRoutine(IEnumerator routine)
        {
            var disposable = routine as IDisposable;
            if (disposable != null) disposable.Dispose();
        }
    }
}
