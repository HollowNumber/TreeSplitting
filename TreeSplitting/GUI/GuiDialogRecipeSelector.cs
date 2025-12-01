using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Gui;

public class GuiDialogRecipeSelector : GuiDialog
{
    public Action<int> OnRecipeSelected;
    List<SkillItem> skillItems = new List<SkillItem>();
    string title;

    public GuiDialogRecipeSelector(ICoreClientAPI capi, IList<ItemStack> recipeOutputs, Action<int> onSelected, string title = "Select Recipe")
        : base(capi)
    {
        OnRecipeSelected = onSelected;
        this.title = title;

        if (recipeOutputs != null)
        {
            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding;

            for (int i = 0; i < recipeOutputs.Count; i++)
            {
                ItemStack stack = recipeOutputs[i];
                ItemSlot dummySlot = new DummySlot(stack); // Used for rendering

                // Fetch description if available (optional, vanilla style)
                string key = stack.Collectible.Code?.Domain + AssetLocation.LocationSeparator + stack.Class.Name() + "craftdesc-" + stack.Collectible.Code?.Path;
                string desc = Lang.GetMatching(key);
                if (desc == key) desc = ""; 

                skillItems.Add(new SkillItem()
                {
                    Code = stack.Collectible.Code.Clone(),
                    Name = stack.GetName(),
                    Description = desc,
                    Data = i, // Store the original index
                    RenderHandler = (AssetLocation code, float dt, double posX, double posY) => {
                        double scsize = GuiElement.scaled(size - 5);
                        capi.Render.RenderItemstackToGui(dummySlot, posX + scsize / 2, posY + scsize / 2, 100, (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize), ColorUtil.WhiteArgb);
                    }
                });
            }
        }
    }

    public override string ToggleKeyCombinationCode => null;
    public override bool PrefersUngrabbedMouse => true;

    public void Compose()
    {
        SingleComposer?.Dispose();

        if (skillItems.Count == 0) return;

        int cols = Math.Min(skillItems.Count, 5);
        int rows = (int)Math.Ceiling(skillItems.Count / (float)cols);
            
        double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding;
        double innerWidth = Math.Max(320, cols * size);
            
        double gridHeight = rows * size;
        // Extra space at bottom for the hover text
        double totalHeight = gridHeight + 80; 

        ElementBounds dialogBounds = ElementBounds.Fixed(0, 0, innerWidth, totalHeight)
            .WithAlignment(EnumDialogArea.CenterMiddle);

        ElementBounds bgBounds = ElementBounds.Fixed(0, 0, innerWidth, totalHeight);
            
        ElementBounds skillGridBounds = ElementBounds.Fixed(10, 40, innerWidth - 20, gridHeight);
        ElementBounds hoverTextBounds = ElementBounds.Fixed(10, gridHeight + 50, innerWidth - 20, 25);

        SingleComposer = capi.Gui.CreateCompo("recipesel", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(title, OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddSkillItemGrid(skillItems, cols, rows, OnGridSlotClick, skillGridBounds, "skillitemgrid")
            .AddDynamicText("", CairoFont.WhiteSmallishText(), hoverTextBounds, "hoverText")
            .EndChildElements()
            .Compose();
            
        // Hook up the hover event
        SingleComposer.GetSkillItemGrid("skillitemgrid").OnSlotOver = OnSlotOver;
    }

    private void OnSlotOver(int index)
    {
        if (index >= 0 && index < skillItems.Count)
        {
            SingleComposer.GetDynamicText("hoverText").SetNewText(skillItems[index].Name);
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private void OnGridSlotClick(int index)
    {
        if (index >= 0 && index < skillItems.Count)
        {
            OnRecipeSelected?.Invoke(index);
            TryClose();
        }
    }

    public override bool TryOpen()
    {
        if (skillItems == null || skillItems.Count == 0) return false;
        Compose();
        return base.TryOpen();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        SingleComposer?.Dispose();
    }
}