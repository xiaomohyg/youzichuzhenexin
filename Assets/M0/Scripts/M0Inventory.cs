using System;
namespace M0
{
    public enum M0Item { Empty, Sword, Sidearm }
    [Serializable] public struct InventorySlot
    {
        public M0Item Item;
        public int Count;
        public string Name => Item == M0Item.Sword ? "像素钢剑" : Item == M0Item.Sidearm ? "脉冲手枪" : "空格";
    }
    // Authority integration: validate changes here before broadcasting the snapshot.
    public sealed class M0Inventory
    {
        public const int HotbarSize = 5, Capacity = 20;
        readonly InventorySlot[] slots = new InventorySlot[Capacity];
        public event Action Changed;
        public uint Revision { get; private set; }
        public int Selected { get; private set; }
        public InventorySlot this[int index] => slots[index];
        public M0Item Equipped => slots[Selected].Item;
        public M0Inventory() { Reset(); }
        public void Reset()
        {
            Array.Clear(slots, 0, slots.Length);
            slots[0] = new InventorySlot { Item = M0Item.Sword, Count = 1 };
            slots[1] = new InventorySlot { Item = M0Item.Sidearm, Count = 1 };
            Selected = 0; Notify();
        }
        void Notify() { Revision++; Changed?.Invoke(); }
        public bool Select(int index)
        {
            if (index < 0 || index >= HotbarSize) return false;
            if (Selected != index) { Selected = index; Notify(); }
            return true;
        }
        public bool Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Capacity || b >= Capacity || a == b) return false;
            var temp = slots[a]; slots[a] = slots[b]; slots[b] = temp; Notify(); return true;
        }
        public bool TryAdd(M0Item item)
        {
            if (item != M0Item.Sword && item != M0Item.Sidearm) return false;
            for (int i = 0; i < Capacity; i++) if (slots[i].Item == M0Item.Empty)
            { slots[i] = new InventorySlot { Item = item, Count = 1 }; Notify(); return true; }
            return false;
        }
        public InventorySlot[] Snapshot() => (InventorySlot[])slots.Clone();
        public void CycleWeapon()
        {
            for (int i = 1; i <= HotbarSize; i++)
            { int next = (Selected + i) % HotbarSize; if (slots[next].Item != M0Item.Empty) { Select(next); return; } }
        }
        public void Scroll(float delta)
        {
            if (Math.Abs(delta) > 0.01f) Select((Selected + (delta > 0 ? HotbarSize - 1 : 1)) % HotbarSize);
        }
    }
}
