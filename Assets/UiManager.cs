﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface IUiEventProcessor
{
    bool ProcessEvent(Event e);
    bool ProcessCustomEvent(CustomEvent e);
}

public interface IUiEventProcessorBackground { }

public interface IUiItemDragger
{
    // should call UiManager.Instance.StartDrag and return true, if there was an item under specified global coordinates.
    bool ProcessStartDrag(float x, float y);
    // should return true if it's possible to drop an item over these coordinates.
    bool ProcessDrag(Item item, float x, float y);
    // should return true if drop was handled at these coordinates.
    bool ProcessDrop(Item item, float x, float y);
    // this is called when dropping succeeded on target
    void ProcessEndDrag();
    // this is called to make sure that source pack still has this item.
    Item ProcessVerifyEndDrag();
    // called when dragging was cancelled, but before it was completed (otherwise rollback is called)
    void ProcessFailDrag();
    // this is called when dropping failed on target (or cancelled, or locked), but after ProcessVerifyEndDrag already happened.
    // i.e. dragging already took the item from this pack, but the other pack did not accept it.
    // in most implementations this returns the item back to it's original index
    void ProcessRollbackDrag(Item item);

}

public interface IUiItemAutoDropper
{
    //
    bool ProcessAutoDrop(Item item);
}

public delegate void UiDragCallback();

public class CustomEvent
{
    // Global: cannot be blocked by lower/higher processors
    public bool IsGlobal = false;
    // Forced: always delivered to all processors, even disabled ones
    public bool IsForced = false;

    protected CustomEvent() { }
}

public class UiManager : MonoBehaviour
{
    private static UiManager _Instance = null;
    public static UiManager Instance
    {
        get
        {
            if (_Instance == null) _Instance = FindObjectOfType<UiManager>();
            return _Instance;
        }
    }

    // each Window occupies certain Z position in the interface layer. Interface layer is a 1..10 field, and a window occupies approximately 0.05 in this field.
    // shadow/background is 0.00, element shadows are 0.02, elements are 0.03, element overlays are 0.04.
    // as such, there's a maximum of 200 windows at once.
    public float TopZ = MainCamera.InterfaceZ + 0.25f;
    private List<MonoBehaviour> Windows = new List<MonoBehaviour>();

    private void UpdateTopZ()
    {
        // this is not needed for drawing anymore, but required for proper UiManager sorting.
        TopZ = MainCamera.InterfaceZ + 0.25f;
        foreach (MonoBehaviour wnd in Windows)
        {
            if (wnd.transform.position.z - 0.001f < TopZ)
                TopZ = wnd.transform.position.z - 0.001f;
        }
        // disable all windows except the topmost one
        if (Windows.Count <= 0) return;
        for (int i = 0; i < Windows.Count - 1; i++)
            Windows[i].gameObject.SetActive(false);
        Windows[Windows.Count - 1].gameObject.SetActive(true);
    }

    public float RegisterWindow(MonoBehaviour wnd)
    {
        Windows.Add(wnd);
        UpdateTopZ();
        return TopZ;
    }

    public void UnregisterWindow(MonoBehaviour wnd)
    {
        Windows.Remove(wnd);
        UpdateTopZ();
    }

    public void SendCustomEvent(CustomEvent e)
    {
        lock (CustomEvents)
        {
            CustomEvents.Enqueue(e);
        }
    }

    public void ClearWindows()
    {
        var windows = new List<MonoBehaviour>();
        windows.AddRange(Windows);
        foreach (MonoBehaviour wnd in windows)
            DestroyImmediate(wnd.gameObject);
        Windows.Clear();
    }

    void Start()
    {

    }

    private Queue<CustomEvent> CustomEvents = new Queue<CustomEvent>();

    private bool GotProcessors = false;
    private List<MonoBehaviour> Processors = new List<MonoBehaviour>();
    private List<bool> ProcessorsEnabled = new List<bool>();

    private IUiEventProcessor lastMouseOver = null;
    private void CheckMouseOver(IUiEventProcessor p)
    {
        if (p != lastMouseOver)
        {
            if (lastMouseOver != null)
            {
                Event ef = new Event();
                ef.type = EventType.MouseMove;
                ef.commandName = "mouseout";
                lastMouseOver.ProcessEvent(ef);
            }
            lastMouseOver = p;
            if (p != null)
            {
                Event ef = new Event();
                ef.type = EventType.MouseMove;
                ef.commandName = "mouseover";
                p.ProcessEvent(ef);
            }
        }
    }

    private float lastMouseX = 0;
    private float lastMouseY = 0;
    private float lastMouseChange = -1;
    private float lastMouseDown = 1;
    void Update()
    {
        lastMouseDown += Time.unscaledDeltaTime;
        if (lastMouseChange >= 0)
            lastMouseChange += Time.unscaledDeltaTime;

        bool mouse1Clicked = Input.GetMouseButton(0);

        GotProcessors = false;
        EnumerateObjects();
        // get all custom events.
        lock (CustomEvents)
        {
            while (CustomEvents.Count > 0)
            {
                CustomEvent ce = CustomEvents.Dequeue();
                for (int i = Processors.Count - 1; i >= 0; i--)
                {
                    // check if processor's renderer is enabled. implicitly don't give any events to invisible objects.
                    if (!ce.IsForced && !ProcessorsEnabled[i]) continue;
                    if (((IUiEventProcessor)Processors[i]).ProcessCustomEvent(ce) && !ce.IsGlobal)
                        break;
                }
            }
        }
        // get all events.
        Event e = new Event();
        while (Event.PopEvent(e))
        {
            // pressing PrintScreen or Alt+S results in screenshot unconditionally.
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Print ||
                 e.keyCode == KeyCode.SysReq ||
                (e.keyCode == KeyCode.S && e.alt)))
            {
                MainCamera.Instance.TakeScreenshot();
                return;
            }

            if (e.rawType == EventType.MouseDown)
            {
                if (lastMouseDown > 0.25f)
                    lastMouseDown = 0;
                else
                {
                    e.commandName = "double";
                    lastMouseDown = 1;
                }
            }

            // reverse iteration
            bool EventIsGlobal = (e.type == EventType.KeyUp ||
                                  e.rawType == EventType.MouseUp);
            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                // check if processor's renderer is enabled. implicitly don't give any events to invisible objects.
                if (!ProcessorsEnabled[i]) continue;
                if (((IUiEventProcessor)Processors[i]).ProcessEvent(e) && !EventIsGlobal)
                    break;
            }
        }

        // also fake mouse event for each processor
        bool doStartDrag = false;
        float doStartDragX = lastMouseX;
        float doStartDragY = lastMouseY;
        Vector2 mPos = Utils.GetMousePosition();
        Object mProcessor = null;
        /*if (mPos.x != lastMouseX ||
            mPos.y != lastMouseY)*/
        {
            Event ef = new Event();
            ef.type = EventType.MouseMove;

            if (lastMouseChange > 0.5f && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                ef.commandName = "tooltip";
                lastMouseChange = -1;
            }

            for (int i = Processors.Count - 1; i >= 0; i--)
            {
                // check if processor's renderer is enabled. implicitly don't give any events to invisible objects.
                if (!ProcessorsEnabled[i]) continue;
                if (((IUiEventProcessor)Processors[i]).ProcessEvent(ef))
                {
                    mProcessor = Processors[i];
                    break;
                }
            }

            if (lastMouseX != mPos.x ||
                lastMouseY != mPos.y)
            {
                lastMouseX = mPos.x;
                lastMouseY = mPos.y;
                doStartDrag = (DragItem == null) ? mouse1Clicked : false;
                lastMouseChange = 0;
                lastMouseDown = 1; // disable doubleclick if drag started
                UnsetTooltip();
            }
        }

        CheckMouseOver((IUiEventProcessor)mProcessor);

        // process drag-drop for items.
        // first ask every element if they can process the drop.
        bool dragProcessed = false;
        //for (int i = Processors.Count - 1; i >= 0; i--)
        if (mProcessor != null && mProcessor is IUiItemDragger)
        {
            _CurrentDragDragger = (IUiItemDragger)mProcessor;

            // start dragging if coordinates changed with mouse button pressed.
            // don't call doStartDrag further if one of the widgets has processed it.
            if (doStartDrag && _CurrentDragDragger.ProcessStartDrag(doStartDragX, doStartDragY))
            {
                doStartDrag = false;
                dragProcessed = true;
                _CurrentDragDragger = null;
                goto NoDrag;
            }

            // process already done dragging (if any)
            if (DragItem != null && _CurrentDragDragger.ProcessDrag(DragItem, mPos.x, mPos.y))
            {
                dragProcessed = true;
                // check drop. if drop is done and processed, we should complete dragging.
                // make new item.
                // it's the duty of _DragDragger to delete the old item based on drag count.
                if (!mouse1Clicked)
                {
                    // if parent has changed, then it probably doesn't belong to the original pack anymore and thus can't be moved.
                    Item newItem = _DragDragger.ProcessVerifyEndDrag();

                    if (newItem == null)
                    {
                        CancelDrag();
                        _CurrentDragDragger = null;
                        goto NoDrag;
                    }

                    if (_CurrentDragDragger.ProcessDrop(newItem, mPos.x, mPos.y))
                    {
                        DragItem = null;
                        DragItemCount = 0;
                        _DragCallback = null;
                        _DragDragger = null;
                    }
                    else
                    {
                        _DragDragger.ProcessRollbackDrag(newItem);
                        CancelDrag();
                    }
                }
            }

            _CurrentDragDragger = null;
        }

        NoDrag:

        if (DragItem != null)
        {
            // cancel drop if we tried to drop on a bad place.
            // DropItem != null && !mouse1Clicked means that it didnt process drop.
            if (!mouse1Clicked)
            {
                CancelDrag();
            }
            else
            {
                DragItem.Class.File_Pack.UpdateSprite();

                if (!dragProcessed) // set mouse cursor
                    MouseCursor.SetCursor(MouseCursor.CurCantPut);
                else MouseCursor.SetCursor(DragItem.Class.File_Pack.File);
            }
        }
    }

    // TOOLTIP RELATED
    private GameObject Tooltip;
    private MeshRenderer TooltipRenderer;
    private MeshFilter TooltipFilter;
    private AllodsTextRenderer TooltipRendererA;
    private Texture2D TooltipBall;
    private Utils.MeshBuilder TooltipBuilder = new Utils.MeshBuilder();

    public void SetTooltip(string text)
    {
        if (Tooltip == null)
        {
            TooltipRendererA = new AllodsTextRenderer(Fonts.Font2, Font.Align.Left, 0, 0, false, 12);
            Tooltip = Utils.CreateObject();
            Tooltip.transform.parent = transform;
            TooltipRenderer = Tooltip.AddComponent<MeshRenderer>();
            TooltipFilter = Tooltip.AddComponent<MeshFilter>();
            TooltipBall = Images.LoadImage("graphics/interface/ball.bmp", 0, Images.ImageType.AllodsBMP);

            Material[] materials = new Material[] { new Material(MainCamera.MainShader), new Material(MainCamera.MainShader)};
            materials[0].mainTexture = TooltipBall;
            TooltipRenderer.materials = materials;

            GameObject TooltipText = TooltipRendererA.GetNewGameObject(0.01f, Tooltip.transform, 100);
            TooltipRendererA.Material.color = new Color32(165, 121, 49, 255);
            TooltipText.transform.localPosition = new Vector3(6, 6, -0.02f);
        }

        Tooltip.SetActive(true);

        float topX = lastMouseX;
        float topY = lastMouseY;

        text = text.Replace('#', '\n').Replace("~", "");
        TooltipRendererA.Text = text;

        // ideal position for the tooltip is top/right of the mouse.
        // but if it doesn't fit, should be moved around.
        topX = lastMouseX;
        topY = lastMouseY - TooltipRendererA.Height - 12;

        float fw = TooltipRendererA.ActualWidth + 12;
        float fh = TooltipRendererA.Height + 12;

        if (topX + fw > MainCamera.Width)
            topX = MainCamera.Width - fw;
        if (topY + fh > MainCamera.Height)
            topY = MainCamera.Height - fh;
        if (topX < 0)
            topX = 0;
        if (topY < 0)
            topY = 0;

        Tooltip.transform.localPosition = new Vector3(topX, topY, MainCamera.MouseZ + 0.01f);

        TooltipBuilder.Reset();
        TooltipBuilder.AddQuad(0, 0, 0, 4, 4);
        TooltipBuilder.AddQuad(0, TooltipRendererA.ActualWidth + 8, 0, 4, 4);
        TooltipBuilder.AddQuad(0, TooltipRendererA.ActualWidth + 8, TooltipRendererA.Height + 6, 4, 4);
        TooltipBuilder.AddQuad(0, 0, TooltipRendererA.Height + 6, 4, 4);

        // now render border quads
        float bw = TooltipRendererA.ActualWidth + 6;
        float bh = TooltipRendererA.Height + 6 - 2; // 2 = difference between "LineHeight" and our custom LineHeight of 12
        // top border bright
        TooltipBuilder.CurrentMesh = 1;
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3, 1);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 1);
        TooltipBuilder.NextVertex();
        // top border dark
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 1 + 2);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3, 1 + 2);
        TooltipBuilder.NextVertex();
        // bottom border bright
        TooltipBuilder.CurrentMesh = 1;
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3, 3 + bh);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 3 + bh);
        TooltipBuilder.NextVertex();
        // bottom border dark
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 3 + bh + 2);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3, 3 + bh + 2);
        TooltipBuilder.NextVertex();
        // left border bright
        TooltipBuilder.CurrentMesh = 1;
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(1, 3);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(1, 3 + bh);
        TooltipBuilder.NextVertex();
        // left border dark
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(1 + 2, 3 + bh);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(1 + 2, 3);
        TooltipBuilder.NextVertex();
        // right border bright
        TooltipBuilder.CurrentMesh = 1;
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 3);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(165, 121, 49, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw, 3 + bh);
        TooltipBuilder.NextVertex();
        // right border dark
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw + 2, 3 + bh);
        TooltipBuilder.NextVertex();
        TooltipBuilder.CurrentColor = new Color32(82, 60, 24, 255);
        TooltipBuilder.CurrentPosition = new Vector3(3 + bw + 2, 3);
        TooltipBuilder.NextVertex();
        // BACKGROUND QUAD
        TooltipBuilder.AddQuad(TooltipBuilder.CurrentMesh, 3, 3, bw, bh, new Color32(33, 44, 33, 255));

        TooltipFilter.mesh = TooltipBuilder.ToMesh(MeshTopology.Quads, MeshTopology.Quads);
    }

    public void UnsetTooltip()
    {
        if (Tooltip != null)
        {
            Tooltip.SetActive(false);
            lastMouseChange = 0f;
        }
    }

    // ITEM DRAGGING RELATED
    private IUiItemDragger _CurrentDragDragger = null;
    private IUiItemDragger _DragDragger = null;
    public Item DragItem { get; private set; }
    public int DragItemCount { get; private set; }
    public long DragMoneyCount { get; private set; }
    private UiDragCallback _DragCallback = null;

    public void StartDrag(Item item, int count, long money, UiDragCallback onCancelDrag)
    {
        if (_CurrentDragDragger == null)
            return; // don't allow drag start if not in callback.
        _DragDragger = _CurrentDragDragger;
        DragItem = item;
        DragItemCount = count;
        DragMoneyCount = money;
        _DragCallback = onCancelDrag;
    }

    public void CancelDrag()
    {
        if (DragItem == null)
            return;
        if (_DragCallback != null)
            _DragCallback();
        _DragDragger.ProcessFailDrag();
        _DragCallback = null;
        DragItem = null;
        DragItemCount = 0;
        _DragDragger = null;
    }

    public void Subscribe(IUiEventProcessor mb)
    {
        if (!Processors.Contains((MonoBehaviour)mb))
            Processors.Add((MonoBehaviour)mb);
    }

    public void Unsubscribe(IUiEventProcessor mb)
    {
        if (lastMouseOver == mb) lastMouseOver = null;
        Processors.Remove((MonoBehaviour)mb);
    }

    void EnumerateObjects()
    {
        if (GotProcessors) return;
        Processors.Sort((a, b) => b.transform.position.z.CompareTo(a.transform.position.z));

        ProcessorsEnabled.Clear();
        int enc = 0;
        foreach (MonoBehaviour mb in Processors)
        {
            if (mb is IUiEventProcessorBackground)
            {
                ProcessorsEnabled.Add(true);
                enc++;
                continue;
            }

            // check if object is active
            if (!mb.gameObject.activeInHierarchy)
            {
                ProcessorsEnabled.Add(false);
                continue;
            }

            // check if object has any visible parts
            List<Renderer> renderers = mb.gameObject.GetComponentsInChildren<Renderer>().Concat(mb.gameObject.GetComponents<Renderer>()).ToList();
            bool isEnabled = renderers.Any(render => render.enabled);

            ProcessorsEnabled.Add(isEnabled);
            if (isEnabled)
                enc++;
        }

        GotProcessors = true;
    }
}