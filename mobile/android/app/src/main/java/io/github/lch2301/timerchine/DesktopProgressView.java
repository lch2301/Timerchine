package io.github.lch2301.timerchine;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Shader;
import android.view.View;

final class DesktopProgressView extends View {
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);private double fraction;private int color=DesktopTheme.ACCENT;private boolean gradient;private Shader gradientFill;
    DesktopProgressView(Context context){super(context);}
    void update(double value,int color,boolean gradient){fraction=Math.max(0,Math.min(1,value));this.color=color;this.gradient=gradient;invalidate();}
    @Override protected void onSizeChanged(int width,int height,int oldWidth,int oldHeight){super.onSizeChanged(width,height,oldWidth,oldHeight);gradientFill=new LinearGradient(0,0,Math.max(1,width),0,0xffffcf5f,0xfff79d42,Shader.TileMode.CLAMP);}
    @Override protected void onDraw(Canvas c){super.onDraw(c);float w=getWidth(),h=getHeight();paint.setShader(null);paint.setColor(DesktopTheme.ELEVATED);c.drawRoundRect(0,0,w,h,h/2,h/2,paint);if(fraction<=0)return;c.save();c.clipRect(0,0,(float)(w*fraction),h);paint.setColor(color);if(gradient)paint.setShader(gradientFill);c.drawRoundRect(0,0,w,h,h/2,h/2,paint);paint.setShader(null);c.restore();}
}
