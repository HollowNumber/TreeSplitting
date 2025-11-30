using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Gui
{
    public class GuiDialogRecipeSelector : GuiDialog
    {
        public Action<int> OnRecipeSelected;
        IList<ItemStack> RecipeOutputs;
        string title;

        public GuiDialogRecipeSelector(ICoreClientAPI capi, IList<ItemStack> recipeOutputs, Action<int> onSelected, string title = "Select Recipe")
            : base(capi)
        {
            RecipeOutputs = recipeOutputs;
            OnRecipeSelected = onSelected;
            this.title = title;
        }

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;

        public void Compose()
        {
            SingleComposer?.Dispose();

            int cols = Math.Min(RecipeOutputs.Count, 5);
            int rows = (int)Math.Ceiling(RecipeOutputs.Count / (float)cols);
            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding;
            double innerWidth = Math.Max(320, cols * size);
            double height = rows * size + 60;

            ElementBounds dialogBounds = ElementBounds.Fixed(0, 0, innerWidth, height)
                .WithAlignment(EnumDialogArea.CenterMiddle);

            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, innerWidth, height);

            // Create dummy inventory for slot grid display
            InventoryGeneric inv = new InventoryGeneric(RecipeOutputs.Count, "recipeslotgrid", capi, null);
            for (int i = 0; i < RecipeOutputs.Count; i++)
            {
                inv[i].Itemstack = RecipeOutputs[i].Clone();
            }

            SingleComposer = capi.Gui.CreateCompo("recipesel", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(inv, OnGridSlotClick, cols, ElementBounds.Fixed(10, 40, innerWidth-20, rows * size), "recipegrid")
                .EndChildElements()
                .Compose();
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }


        private void OnGridSlotClick(object slotIdObj)
        {
            if (slotIdObj is int slotId && slotId >= 0 && slotId < RecipeOutputs.Count)
            {
                OnRecipeSelected?.Invoke(slotId);
                base.TryClose();
            }
        }

        public override bool TryOpen()
        {
            Compose();
            return base.TryOpen();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            SingleComposer?.Dispose();
        }
    }
}