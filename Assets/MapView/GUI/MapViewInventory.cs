﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using UnityEngine;

public class MapViewInventory : MonoBehaviour, IUiEventProcessor
{
    public ItemView View { get; private set; }
    private MeshRenderer Renderer;
    private MeshFilter Filter;

    private int ArrowHovered = 0;
    private int ArrowClicked = 0;

    private static Texture2D InvFrame = null;
    private static Texture2D InvArrow1 = null;
    private static Texture2D InvArrow2 = null;
    private static Texture2D InvArrow3 = null;
    private static Texture2D InvArrow4 = null;

    private Stopwatch LastScroll = null;
    public int ItemCount = 0;

    private void SendItemMoveCommand(Item item, int index)
    {
        // check if this pack is unit's pack.
        if (View.Pack.Parent == null ||
            View.Pack != View.Pack.Parent.ItemsPack) return;

        // send command.
        // first off, determine move source.
        ServerCommands.ItemMoveLocation from;
        int fromIndex = -1;
        ServerCommands.ItemMoveLocation to;
        int toIndex = -1;

        MapUnit unit = View.Pack.Parent;

        if (item.Parent == unit.ItemsBody)
        {
            from = ServerCommands.ItemMoveLocation.UnitBody;
            fromIndex = item.Class.Option.Slot;
        }
        else if (item.Parent == unit.ItemsPack)
        {
            from = ServerCommands.ItemMoveLocation.UnitPack;
            fromIndex = item.Index;
        }
        else if (item.Parent.LocationHint != ServerCommands.ItemMoveLocation.Undefined)
        {
            from = item.Parent.LocationHint;
            fromIndex = item.Index;
        }
        else from = ServerCommands.ItemMoveLocation.Ground;

        to = ServerCommands.ItemMoveLocation.UnitPack;
        toIndex = index;

        Client.SendItemMove(from, to, fromIndex, toIndex, item.Count, unit, MapView.Instance.MouseCellX, MapView.Instance.MouseCellY);
    }

    public void Awake()
    {
        LastScroll = new Stopwatch();

        UiManager.Instance.Subscribe(this);
        View = Utils.CreateObjectWithScript<ItemView>();
        View.transform.parent = transform;
        View.transform.localScale = new Vector3(1, 1, 1);
        View.transform.localPosition = new Vector3(32, 6, -2);

        View.OnProcessDrop = (Item item, int index) =>
        {
            SendItemMoveCommand(item, index);
            View.Pack.PutItem(index, item);
            return true;
        };
    }

    public void Start()
    {
        if (ItemCount == 0) ItemCount = (MainCamera.Width - 176 - 64) / 80; // each arrow is 32 in width
        View.InvScale = 1;
        View.InvWidth = (int)(ItemCount / View.InvScale);
        View.InvHeight = (int)(1 / View.InvScale);
        View.ShowMoney = true;

        if (InvFrame == null) InvFrame = Images.LoadImage("graphics/interface/invframe.bmp", 0, Images.ImageType.AllodsBMP);
        if (InvArrow1 == null) InvArrow1 = Images.LoadImage("graphics/interface/invarrow1.bmp", 0, Images.ImageType.AllodsBMP);
        if (InvArrow2 == null) InvArrow2 = Images.LoadImage("graphics/interface/invarrow2.bmp", 0, Images.ImageType.AllodsBMP);
        if (InvArrow3 == null) InvArrow3 = Images.LoadImage("graphics/interface/invarrow3.bmp", 0, Images.ImageType.AllodsBMP);
        if (InvArrow4 == null) InvArrow4 = Images.LoadImage("graphics/interface/invarrow4.bmp", 0, Images.ImageType.AllodsBMP);

        //
        Renderer = gameObject.AddComponent<MeshRenderer>();
        Filter = gameObject.AddComponent<MeshFilter>();

        Renderer.materials = new Material[] { new Material(MainCamera.MainShader), new Material(MainCamera.MainShader), new Material(MainCamera.MainShader) };
        Renderer.materials[0].mainTexture = InvFrame;
        Renderer.materials[1].color = Renderer.materials[2].color = new Color(0, 0, 0, 0);

        // generate mesh.
        Utils.MeshBuilder mb = new Utils.MeshBuilder();
        // 3 submeshes: left arrow, right arrow, and background. I'm NOT using the full original inventory view.
        for (int j = 0; j >= 0; j--)
        {
            for (int i = 0; i < View.InvWidth; i++)
            {
                int internalPosition;
                if (i == 0) internalPosition = 0;
                else if (i == View.InvWidth - 1) internalPosition = 4;
                else internalPosition = (i % 3) + 1;
                int yoffs = 0;
                if (View.InvScale < 1)
                    yoffs = (int)(1.5f / View.InvScale);
                Rect internalRect = Utils.DivRect(new Rect(32 + internalPosition * 80, 0, 80, 90), new Vector2(InvFrame.width, InvFrame.height));
                mb.AddQuad(0, 32 + i * 80 * View.InvScale, j * 80 * View.InvScale + yoffs, 80 * View.InvScale, 90 * View.InvScale, internalRect);
            }
        }

        // add two quads for unpressed buttons
        mb.AddQuad(0, 0, 0, 32, 90, Utils.DivRect(new Rect(0, 0, 32, 90), new Vector2(InvFrame.width, InvFrame.height)));
        mb.AddQuad(0, 32 + View.InvWidth * 80 * View.InvScale, 0, 32, 90, Utils.DivRect(new Rect(432, 0, 32, 90), new Vector2(InvFrame.width, InvFrame.height)));

        mb.AddQuad(1, 0, 2, 32, 88);
        mb.AddQuad(2, 32 + View.InvWidth * 80 * View.InvScale, 2, 32, 88);

        Filter.mesh = mb.ToMesh(MeshTopology.Quads, MeshTopology.Quads, MeshTopology.Quads);
    }

    public void OnDestroy()
    {
        UiManager.Instance.Unsubscribe(this);
    }

    public bool ProcessEvent(Event e)
    {
        if (e.rawType == EventType.MouseDown ||
            e.rawType == EventType.MouseUp ||
            e.rawType == EventType.MouseMove)
        {
            int lw = (int)(64 + View.InvWidth * 80 * View.InvScale);
            int lh = 90;

            Vector2 mPos = Utils.GetMousePosition();
            Vector2 mPosLocal = mPos - new Vector2(transform.position.x, transform.position.y);
            if (mPosLocal.x < 0 || mPosLocal.y < 0 ||
                mPosLocal.x > lw || mPosLocal.y > lh)
            {
                if (ArrowHovered != 0)
                {
                    ArrowHovered = 0;
                    UpdateMaterials();
                }

                return false;
            }

            // process hover
            if (e.rawType == EventType.MouseMove)
            {
                if (mPosLocal.x < 32)
                    ArrowHovered = 1;
                else if (mPosLocal.x >= lw - 32)
                    ArrowHovered = 2;
                else ArrowHovered = 0;
                UpdateMaterials();
            }
            else if (e.rawType == EventType.MouseDown)
            {
                if (ArrowClicked != ArrowHovered)
                {
                    ArrowClicked = ArrowHovered;
                    UpdateMaterials();
                }
            }
            else if (e.rawType == EventType.MouseUp)
            {
                if (ArrowClicked != 0)
                {
                    ArrowClicked = 0;
                    UpdateMaterials();
                }
            }

            MouseCursor.SetCursor(MouseCursor.CurDefault);
            return true;
        }

        return false;
    }

    public bool ProcessCustomEvent(CustomEvent ce)
    {
        if (ce is MapViewSelectionChanged sel)
        {
            MapObject selectedObject = sel.NewSelection.Count > 0 ? sel.NewSelection[0] : null;
            if (selectedObject is MapHuman h && h.IsHero)
                SetPack(h);
            else SetPack(null);
        }
        return false;
    }

    private void UpdateMaterials()
    {
        if (ArrowHovered == 1 || ArrowClicked == 1)
        {
            Renderer.materials[1].mainTexture = (ArrowClicked == 1) ? InvArrow3 : InvArrow1;
            Renderer.materials[1].color = new Color(1, 1, 1, 1);
        }
        else Renderer.materials[1].color = new Color(0, 0, 0, 0);

        if (ArrowHovered == 2 || ArrowClicked == 2)
        {
            Renderer.materials[2].mainTexture = (ArrowClicked == 2) ? InvArrow4 : InvArrow2;
            Renderer.materials[2].color = new Color(1, 1, 1, 1);
        }
        else Renderer.materials[2].color = new Color(0, 0, 0, 0);
    }

    public void Update()
    {
        if (View.Pack != null && View.AutoDropTarget == null &&
            View.Pack.Parent != null && View.Pack.Parent.Player == MapLogic.Instance.ConsolePlayer)
        {
            View.AutoDropTarget = (IUiItemAutoDropper)View.Pack.Parent.GameScript;
        }

        if (ArrowClicked != 0)
        {
            if (!LastScroll.IsRunning || LastScroll.ElapsedMilliseconds > 100)
            {
                if (ArrowClicked == 1) // left arrow
                {
                    if (View.Scroll > 0)
                        View.Scroll--;
                }
                else if (ArrowClicked == 2) // right arrow
                {
                    if (View.Scroll < View.GetVisualPackCount() - View.InvWidth)
                        View.Scroll++;
                }
                LastScroll.Restart();
            }
        }
    }

    public void SetPack(MapHuman human)
    {
        if (human != null && human.Player == MapLogic.Instance.ConsolePlayer)
        {
            View.Pack = human.ItemsPack;
            View.AutoDropTarget = (IUiItemAutoDropper)human.GameScript; // may be null at this point
        }
        else
        {
            View.Pack = null;
            View.AutoDropTarget = null;
        }
    }
}