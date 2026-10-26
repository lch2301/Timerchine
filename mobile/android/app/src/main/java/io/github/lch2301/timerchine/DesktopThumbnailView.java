package io.github.lch2301.timerchine;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.drawable.GradientDrawable;
import android.widget.ImageView;

/** The desktop's fitted thumbnail and literal No Data placeholder. */
final class DesktopThumbnailView extends ImageView {
    private final Paint label=new Paint(Paint.ANTI_ALIAS_FLAG);
    DesktopThumbnailView(Context context){super(context);DesktopTheme theme=new DesktopTheme(context);setScaleType(ScaleType.FIT_CENTER);GradientDrawable background=theme.shape(DesktopTheme.ELEVATED,7);background.setStroke(theme.dp(1),DesktopTheme.BORDER);setBackground(background);setClipToOutline(true);label.setColor(DesktopTheme.MUTED);label.setTypeface(theme.regular);label.setTextSize(theme.dp(9));label.setTextAlign(Paint.Align.CENTER);setContentDescription("No Data");}
    @Override protected void onDraw(Canvas canvas){super.onDraw(canvas);if(getDrawable()==null)canvas.drawText("No Data",getWidth()/2f,getHeight()/2f-(label.ascent()+label.descent())/2,label);}
}
