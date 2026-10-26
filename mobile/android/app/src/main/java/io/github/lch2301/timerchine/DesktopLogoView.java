package io.github.lch2301.timerchine;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.Shader;
import android.view.View;

final class DesktopLogoView extends View {
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);private final Path path=new Path();
    private final Shader tileFill=new LinearGradient(0,0,96,96,0xff233339,0xff101921,Shader.TileMode.CLAMP);
    private final Shader glassFill=new LinearGradient(24,24,72,72,0x2670e0c5,0x0870e0c5,Shader.TileMode.CLAMP);
    private final Shader sandFill=new LinearGradient(36,35,59,66,0xffbffbe7,0xff5bcfb1,Shader.TileMode.CLAMP);
    DesktopLogoView(Context context){super(context);setContentDescription("타이머신 로고");}
    @Override protected void onDraw(Canvas c){super.onDraw(c);float size=Math.min(getWidth(),getHeight());c.save();c.translate((getWidth()-size)/2,(getHeight()-size)/2);c.scale(size/96,size/96);
        paint.setStyle(Paint.Style.FILL);paint.setShader(tileFill);c.drawRoundRect(2,2,94,94,22,22,paint);paint.setShader(null);
        paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(1.4f);paint.setColor(0xff426263);c.drawRoundRect(2,2,94,94,22,22,paint);
        path.reset();path.moveTo(29,26);path.lineTo(67,26);path.cubicTo(67,38,56,42,51,48);path.cubicTo(56,54,67,58,67,70);path.lineTo(29,70);path.cubicTo(29,58,40,54,45,48);path.cubicTo(40,42,29,38,29,26);path.close();
        paint.setStyle(Paint.Style.FILL);paint.setShader(glassFill);c.drawPath(path,paint);paint.setShader(null);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(3.5f);paint.setColor(0xff99efd8);c.drawPath(path,paint);
        paint.setStyle(Paint.Style.FILL);paint.setShader(sandFill);path.reset();path.moveTo(36,34);path.lineTo(60,34);path.lineTo(48,43);path.close();c.drawPath(path,paint);
        path.reset();path.moveTo(35,64);path.lineTo(48,54);path.lineTo(61,64);path.close();c.drawPath(path,paint);paint.setShader(null);
        paint.setStrokeWidth(1.6f);paint.setColor(0xff95ebd0);c.drawLine(48,45,48,55,paint);paint.setStrokeWidth(4.4f);paint.setStrokeCap(Paint.Cap.ROUND);paint.setColor(DesktopTheme.ACCENT);c.drawLine(26,20,70,20,paint);c.drawLine(26,76,70,76,paint);c.restore();}
}
