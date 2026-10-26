package io.github.lch2301.timerchine;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.view.Gravity;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.TextView;

/** The desktop 1.2.0 palette, typography, controls and glyph vocabulary. */
final class DesktopTheme {
    static final int BACKGROUND=0xff0e1218, CARD=0xff171d26, ELEVATED=0xff1f2732, BORDER=0xff2f3b49;
    static final int FOREGROUND=0xffedf4f9, MUTED=0xff8b9cae, STOP=0xffaab8c5, INPUT=0xff263542;
    static final int ACCENT=0xff70e0c5, INK=0xff112b26, WARM=0xfff6c06d, ERROR=0xffffa0a0;
    final Context context;
    final Typeface regular, bold, time;
    DesktopTheme(Context context) {
        this.context=context;
        regular=font("fonts/NanumSquareRoundR.ttf",Typeface.DEFAULT);
        bold=font("fonts/NanumSquareRoundB.ttf",Typeface.DEFAULT_BOLD);
        time=font("fonts/NunitoTimer-Bold.ttf",Typeface.MONOSPACE);
    }
    int dp(float value) { return Math.round(value*context.getResources().getDisplayMetrics().density); }
    Typeface font(String path,Typeface fallback) { try { return Typeface.createFromAsset(context.getAssets(),path); } catch(RuntimeException missing) { return fallback; } }
    LinearLayout column() { LinearLayout v=new LinearLayout(context);v.setOrientation(LinearLayout.VERTICAL);return v; }
    LinearLayout row() { LinearLayout v=new LinearLayout(context);v.setGravity(Gravity.CENTER_VERTICAL);return v; }
    TextView text(String value,float size,int color) { TextView v=new TextView(context);v.setText(value);v.setTextSize(size);v.setTextColor(color);v.setTypeface(regular);v.setLineSpacing(dp(2),1);return v; }
    GradientDrawable shape(int color,int radius) { GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(radius));return d; }
    LinearLayout.LayoutParams fill() { return new LinearLayout.LayoutParams(-1,-2); }
    LinearLayout.LayoutParams weight() { return new LinearLayout.LayoutParams(0,-2,1); }
    Button button(String label,String glyph,boolean primary,boolean quiet) {
        Button v=new Button(context);v.setText(label);v.setAllCaps(false);v.setTypeface(bold);v.setTextSize(12);
        v.setMinWidth(0);v.setMinimumWidth(0);v.setMinHeight(dp(40));v.setMinimumHeight(dp(40));
        v.setPadding(dp(quiet?5:11),dp(7),dp(quiet?5:11),dp(7));style(v,glyph,primary,quiet,false);return v;
    }
    void style(Button v,String glyph,boolean primary,boolean quiet,boolean alarming) {
        int color=primary?INK:quiet?MUTED:FOREGROUND;
        v.setTextColor(color);GradientDrawable background=shape(quiet?CARD:alarming?WARM:primary?ACCENT:ELEVATED,10);if(!primary&&!quiet)background.setStroke(dp(1),BORDER);v.setBackground(background);
        if(glyph!=null) { Glyph icon=new Glyph(glyph,"stop".equals(glyph)&&!primary?STOP:color,dp(13));v.setCompoundDrawablesWithIntrinsicBounds(icon,null,null,null);v.setCompoundDrawablePadding(dp(5)); }
        else v.setCompoundDrawables(null,null,null,null);
    }
    EditText input(String value,String hint) { EditText v=new EditText(context);v.setText(value);v.setHint(hint);v.setSingleLine(true);v.setTextColor(FOREGROUND);v.setHintTextColor(MUTED);v.setTextSize(13);v.setTypeface(regular);v.setPadding(dp(10),dp(9),dp(10),dp(9));GradientDrawable normal=shape(ELEVATED,12),focused=shape(ELEVATED,12);normal.setStroke(dp(1),BORDER);focused.setStroke(dp(1),ACCENT);StateListDrawable background=new StateListDrawable();background.addState(new int[]{android.R.attr.state_focused},focused);background.addState(new int[]{},normal);v.setBackground(background);return v; }
    RadioButton radio(String label) { RadioButton v=new RadioButton(context);v.setId(android.view.View.generateViewId());v.setText(label);v.setTextColor(FOREGROUND);v.setTextSize(12);v.setTypeface(regular);v.setButtonTintList(ColorStateList.valueOf(ACCENT));v.setMinHeight(dp(38));return v; }
    CheckBox check(String label,boolean selected) { CheckBox v=new CheckBox(context);v.setText(label);v.setTextColor(FOREGROUND);v.setTextSize(13);v.setTypeface(regular);v.setButtonTintList(ColorStateList.valueOf(ACCENT));v.setChecked(selected);v.setMinHeight(dp(40));return v; }
    static final class Glyph extends Drawable {
        private final String kind;private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);private final Path path=new Path();private final int size;
        Glyph(String kind,int color,int size){this.kind=kind;this.size=size;paint.setColor(color);paint.setStrokeWidth(1.7f);paint.setStrokeCap(Paint.Cap.ROUND);paint.setStrokeJoin(Paint.Join.ROUND);}
        @Override public int getIntrinsicWidth(){return size;} @Override public int getIntrinsicHeight(){return size;}
        @Override public void draw(Canvas c){Rect b=getBounds();c.save();c.translate(b.left,b.top);c.scale(b.width()/24f,b.height()/24f);paint.setStyle(Paint.Style.STROKE);path.reset();
            switch(kind){
                case "play":paint.setStyle(Paint.Style.FILL);path.moveTo(8,4.5f);path.lineTo(20,12);path.lineTo(8,19.5f);path.close();c.drawPath(path,paint);break;
                case "stop":paint.setStyle(Paint.Style.FILL);c.drawRect(5,5,19,19,paint);break;
                case "add":c.drawLine(12,5,12,19,paint);c.drawLine(5,12,19,12,paint);break;
                case "save":case "check":path.moveTo(5,12);path.lineTo(10,17);path.lineTo(20,6);c.drawPath(path,paint);break;
                case "delete":c.drawLine(5,6,19,6,paint);path.moveTo(9,6);path.lineTo(9,3);path.lineTo(15,3);path.lineTo(15,6);c.drawPath(path,paint);path.reset();path.moveTo(7,6);path.lineTo(8,20);path.lineTo(16,20);path.lineTo(17,6);c.drawPath(path,paint);c.drawLine(10,10,10,16,paint);c.drawLine(14,10,14,16,paint);break;
                case "folder":path.moveTo(3,18);path.lineTo(3,6);path.lineTo(10,6);path.lineTo(12,9);path.lineTo(21,9);path.lineTo(19,19);path.lineTo(3,19);path.lineTo(6,11);path.lineTo(20,11);c.drawPath(path,paint);break;
                case "bell":path.moveTo(6,16);path.cubicTo(7,13,4,5,12,5);path.cubicTo(20,5,17,13,18,16);c.drawPath(path,paint);c.drawLine(5,17,19,17,paint);c.drawArc(9,17,15,22,0,180,false,paint);c.drawLine(12,3,12,5,paint);break;
                case "restart":c.drawArc(5,5,19,19,-75,300,false,paint);path.moveTo(15,2);path.lineTo(15,7);path.lineTo(20,7);c.drawPath(path,paint);break;
                case "settings":c.drawCircle(12,12,4,paint);for(int i=0;i<8;i++){double a=i*Math.PI/4;c.drawLine(12+(float)Math.cos(a)*8,12+(float)Math.sin(a)*8,12+(float)Math.cos(a)*10,12+(float)Math.sin(a)*10,paint);}c.drawCircle(12,12,7.5f,paint);break;
                case "close":c.drawLine(6,6,18,18,paint);c.drawLine(6,18,18,6,paint);break;
                case "edit":path.moveTo(13,4);path.lineTo(4,4);path.cubicTo(2.9f,4,2,4.9f,2,6);path.lineTo(2,20);path.cubicTo(2,21.1f,2.9f,22,4,22);path.lineTo(18,22);path.cubicTo(19.1f,22,20,21.1f,20,20);path.lineTo(20,12);c.drawPath(path,paint);path.reset();path.moveTo(8,17);path.lineTo(9.5f,12);path.lineTo(19.5f,2);path.lineTo(22,4.5f);path.lineTo(12,14.5f);path.close();c.drawPath(path,paint);c.drawLine(9.5f,12,12,14.5f,paint);c.drawLine(17.6f,3.9f,20.1f,6.4f,paint);break;
            }c.restore();}
        @Override public void setAlpha(int alpha){paint.setAlpha(alpha);}@Override public void setColorFilter(ColorFilter filter){paint.setColorFilter(filter);}@Override public int getOpacity(){return PixelFormat.TRANSLUCENT;}
    }
}
