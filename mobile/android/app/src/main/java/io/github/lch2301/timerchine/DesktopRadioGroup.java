package io.github.lch2301.timerchine;

import android.content.Context;
import android.view.View;
import android.widget.RadioGroup;

/** The desktop FlowLayoutPanel wraps source options without changing their order. */
final class DesktopRadioGroup extends RadioGroup {
    DesktopRadioGroup(Context context){super(context);setOrientation(HORIZONTAL);}
    @Override protected void onMeasure(int widthSpec,int heightSpec){
        int limit=MeasureSpec.getSize(widthSpec)-getPaddingLeft()-getPaddingRight();int used=0,rowHeight=0,total=getPaddingTop()+getPaddingBottom(),maximum=0;
        for(int i=0;i<getChildCount();i++){View child=getChildAt(i);if(child.getVisibility()==GONE)continue;measureChild(child,widthSpec,heightSpec);int width=child.getMeasuredWidth(),height=child.getMeasuredHeight();if(used>0&&used+width>limit){maximum=Math.max(maximum,used);total+=rowHeight;used=0;rowHeight=0;}used+=width;rowHeight=Math.max(rowHeight,height);}
        maximum=Math.max(maximum,used);total+=rowHeight;setMeasuredDimension(resolveSize(maximum+getPaddingLeft()+getPaddingRight(),widthSpec),resolveSize(total,heightSpec));
    }
    @Override protected void onLayout(boolean changed,int left,int top,int right,int bottom){int x=getPaddingLeft(),y=getPaddingTop(),rowHeight=0,limit=right-left-getPaddingRight();for(int i=0;i<getChildCount();i++){View child=getChildAt(i);if(child.getVisibility()==GONE)continue;int width=child.getMeasuredWidth(),height=child.getMeasuredHeight();if(x>getPaddingLeft()&&x+width>limit){x=getPaddingLeft();y+=rowHeight;rowHeight=0;}child.layout(x,y,x+width,y+height);x+=width;rowHeight=Math.max(rowHeight,height);}}
}
