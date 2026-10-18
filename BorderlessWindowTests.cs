namespace RidersToolkit;

internal static class BorderlessWindowTests
{
    static void Require(bool value){if(!value)throw new Exception("Borderless window assertion failed");}
    static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected the display operation to be refused");}
    internal static void Run(Action<string,Action> check)
    {
        check("Borderless fills the complete current monitor and restores exact window chrome and bounds",()=>
        {
            var api=new Fake();var before=api.Current;var mode=new BorderlessGameWindow(api);mode.Enable(before.Handle);
            Require(mode.Active&&api.Current.Bounds==before.Screen&&(api.Current.Style&0xCF0000)==0&&(api.Current.Style&0x80000000)!=0);
            mode.Restore();Require(!mode.Active&&api.Current==before);
        });
        check("Exclusive fullscreen, minimized, maximized and child windows cannot be converted accidentally",()=>
        {
            foreach(uint style in new[]{0x90000000u,0x36CF0000u,0x17CF0000u,0x56CF0000u})
            {var api=new Fake();api.Current=api.Current with{Style=style};var mode=new BorderlessGameWindow(api);Reject(()=>mode.Enable(api.Current.Handle));Require(api.Writes==0&&!mode.Active);}
        });
        check("Failed borderless resize restores the original window instead of leaving it frameless",()=>
        {
            var api=new Fake{FailNextBounds=true};var before=api.Current;var mode=new BorderlessGameWindow(api);
            Reject(()=>mode.Enable(before.Handle));Require(api.Current==before&&!mode.Active);
        });
        check("Restoration refuses a recycled handle, new process or user display change without writing",()=>
        {
            foreach(int scenario in Enumerable.Range(0,4))
            {
                var api=new Fake();var mode=new BorderlessGameWindow(api);mode.Enable(api.Current.Handle);
                api.Current=scenario switch{0=>api.Current with{Pid=99},1=>api.Current with{Started=999},2=>api.Current with{Style=0x16CF0000},_=>api.Current with{Bounds=new(0,0,800,600)}};
                var changed=api.Current;int writes=api.Writes;Reject(mode.Restore);Require(api.Writes==writes&&api.Current==changed&&!mode.Active);
            }
        });
        check("Borderless uses physical pixels correctly on a monitor left of the primary screen",()=>
        {
            var api=new Fake();api.Current=api.Current with{Screen=new(-2560,-180,0,1260)};var before=api.Current;var mode=new BorderlessGameWindow(api);
            mode.Enable(before.Handle);Require(api.Current.Bounds.Width==2560&&api.Current.Bounds.Left==-2560&&api.Current.Bounds.Top==-180);mode.Restore();Require(api.Current==before);
        });
    }
    sealed class Fake:IGameWindowApi
    {
        public GameWindowSnapshot Current=new(123,42,100,0x16CF0000,new(40,60,1320,800),new(0,0,1920,1080));
        public int Writes;public bool FailNextBounds;
        public GameWindowSnapshot Capture(nint window)=>Current;
        public void Style(nint window,uint style){Writes++;Current=Current with{Style=style};}
        public void Bounds(nint window,WindowPixels bounds){if(FailNextBounds){FailNextBounds=false;throw new InvalidOperationException("Injected resize failure");}Writes++;Current=Current with{Bounds=bounds};}
    }
}
