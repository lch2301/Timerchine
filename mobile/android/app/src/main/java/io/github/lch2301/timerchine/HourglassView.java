package io.github.lch2301.timerchine;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RadialGradient;
import android.graphics.Shader;
import android.os.SystemClock;
import android.util.AttributeSet;
import android.view.View;
import io.github.lch2301.timerchine.core.TimerState;
import io.github.lch2301.timerchine.core.TimerEngine;

/** PC hourglass geometry and its initial turn, warm sand and paused stillness. */
public final class HourglassView extends View {
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path glass=new Path(),upper=new Path(),lower=new Path(),mound=new Path(),reflection=new Path(),moundSurface=new Path();
    private final Shader glassFill=new LinearGradient(-39,0,39,0,0xff21393d,0xff16242c,Shader.TileMode.CLAMP);
    private final Shader sandFill=new LinearGradient(-35,-60,35,60,0xffffdfa5,0xffdfa350,Shader.TileMode.CLAMP);
    private final Shader halo=new RadialGradient(0,0,90,0x1a70e0c5,0x0070e0c5,Shader.TileMode.CLAMP);
    private final Shader shadow=new RadialGradient(0,0,66,0x4c000000,0x00000000,Shader.TileMode.CLAMP);
    private final Shader reflectedLight=new LinearGradient(-35,-60,20,60,0x23a6f5e0,0x00a6f5e0,Shader.TileMode.CLAMP);
    private final Shader upperCap=new LinearGradient(0,-78,0,-67,0xff547b7c,0xff263e46,Shader.TileMode.CLAMP);
    private final Shader lowerCap=new LinearGradient(0,67,0,78,0xff547b7c,0xff263e46,Shader.TileMode.CLAMP);
    private TimerState state;
    public HourglassView(Context context){super(context);initialize();}
    public HourglassView(Context context,AttributeSet attrs){super(context,attrs);initialize();}
    private void initialize(){
        setContentDescription("모래시계 타이머");
        glass.moveTo(-39,-61);glass.lineTo(39,-61);glass.lineTo(39,-55);glass.lineTo(5,-3);glass.lineTo(5,3);glass.lineTo(39,55);glass.lineTo(39,61);glass.lineTo(-39,61);glass.lineTo(-39,55);glass.lineTo(-5,3);glass.lineTo(-5,-3);glass.lineTo(-39,-55);glass.close();
        upper.moveTo(-36,-57);upper.lineTo(36,-57);upper.lineTo(4,-3);upper.lineTo(-4,-3);upper.close();
        lower.moveTo(-4,3);lower.lineTo(4,3);lower.lineTo(36,57);lower.lineTo(-36,57);lower.close();
        reflection.moveTo(-34,-62);reflection.lineTo(-19,-62);reflection.lineTo(21,62);reflection.lineTo(7,62);reflection.close();
        Matrix shadowTransform=new Matrix();shadowTransform.setScale(1,8f/66);shadowTransform.postTranslate(0,87);shadow.setLocalMatrix(shadowTransform);
    }
    public void setState(TimerState timer){state=timer;invalidate();}
    /** Kept for callers that only need a static ratio. */
    public void setProgress(int remaining,int duration,boolean active){
        TimerState timer=new TimerState();timer.spec.durationSeconds=duration;timer.remainingSeconds=remaining;timer.phase=active?TimerState.Phase.RUNNING:TimerState.Phase.STOPPED;timer.deadlineElapsedMs=SystemClock.elapsedRealtime()+remaining*1000L;setState(timer);
    }
    @Override protected void onDraw(Canvas canvas){
        super.onDraw(canvas);if(state==null||getWidth()<4||getHeight()<4)return;
        double duration=state.spec.durationSeconds;
        double remaining=TimerEngine.remainingMilliseconds(state,SystemClock.elapsedRealtime())/1000d;
        double elapsed=Math.max(0,Math.min(duration,duration-remaining)),upperSand=0,lowerSand=1;
        float rotation=0;boolean flowing=false;
        if(state.phase!=TimerState.Phase.IDLE&&duration>0&&state.phase!=TimerState.Phase.ALARMING&&elapsed<duration){
            double flipDuration=Math.min(.7,duration*.25);
            if(elapsed<flipDuration){double p=elapsed/flipDuration;rotation=(float)(180*p*p*(3-2*p));}
            else{double transfer=Math.max(0,Math.min(1,(elapsed-flipDuration)/(duration-flipDuration)));upperSand=1-transfer;lowerSand=transfer;flowing=state.phase==TimerState.Phase.RUNNING&&transfer<1;}
        }
        canvas.save();float scale=Math.max(.01f,Math.min((getWidth()-10)/214f,(getHeight()-6)/214f));canvas.translate(getWidth()/2f,getHeight()/2f);canvas.scale(scale,scale);
        fill(DesktopTheme.CARD);paint.setShader(halo);canvas.drawCircle(0,0,90,paint);paint.setShader(shadow);canvas.drawOval(-66,79,66,95,paint);paint.setShader(null);
        canvas.rotate(rotation);paint.setShader(glassFill);canvas.drawPath(glass,paint);paint.setShader(null);canvas.save();canvas.clipPath(glass);paint.setShader(reflectedLight);canvas.drawPath(reflection,paint);paint.setShader(null);canvas.restore();
        double slope=64d/54,area=2160;
        float upperSurface=(float)(-3-(-8+Math.sqrt(64+2*slope*area*upperSand))/slope);
        float lowerSurface=(float)(57-(72-Math.sqrt(Math.max(0,5184-2*slope*area*lowerSand)))/slope);
        if(upperSand>0){canvas.save();canvas.clipPath(upper);paint.setShader(sandFill);canvas.drawRect(-40,upperSurface,40,1,paint);paint.setShader(null);stroke(0xc8ffeabd,1.1f);canvas.drawLine(-40,upperSurface+.5f,40,upperSurface+.5f,paint);canvas.restore();}
        if(lowerSand>0){canvas.save();canvas.clipPath(lower);fill(0xffffdfa5);paint.setShader(sandFill);canvas.drawRect(-40,lowerSurface,40,62,paint);float peak=(float)(7*Math.sin(Math.PI*lowerSand));mound.reset();mound.moveTo(-16,lowerSurface+1);mound.lineTo(0,lowerSurface-peak);mound.lineTo(16,lowerSurface+1);mound.close();canvas.drawPath(mound,paint);paint.setShader(null);stroke(0xc8ffeabd,1.1f);moundSurface.reset();moundSurface.moveTo(-36,lowerSurface+.5f);moundSurface.lineTo(-16,lowerSurface+.5f);moundSurface.lineTo(0,lowerSurface-peak+.5f);moundSurface.lineTo(16,lowerSurface+.5f);moundSurface.lineTo(36,lowerSurface+.5f);canvas.drawPath(moundSurface,paint);fill(0x5ffff0cb);for(int i=0;i<15;i++){float x=-31+i*19%62,y=12+i*17%43;if(y>lowerSurface+2)canvas.drawCircle(x,y,.45f,paint);}canvas.restore();}
        if(flowing){stroke(0x20ffda99,4);canvas.drawLine(0,-3,0,Math.max(4,lowerSurface),paint);stroke(0xe6ffdea2,1.15f);canvas.drawLine(0,-3,0,Math.max(4,lowerSurface),paint);fill(0xffffedc1);for(int i=0;i<5;i++){double position=(elapsed*3.4+i/5d)%1;float y=(float)(3+position*Math.max(1,lowerSurface-3)),x=(float)(Math.sin(elapsed*13+i)*.9);canvas.drawCircle(x,y,1,paint);}}
        stroke(0x8281b8b7,1.5f);canvas.drawPath(glass,paint);stroke(0x73cffaef,1.3f);canvas.drawLine(-33,-53,-12,-19,paint);canvas.drawLine(33,53,12,19,paint);stroke(0x5aa6e6d5,.85f);canvas.drawLine(-5,0,5,0,paint);
        stroke(0xff364f55,5);canvas.drawLine(-49,-68,-49,68,paint);canvas.drawLine(49,-68,49,68,paint);stroke(0x7385aaab,1);canvas.drawLine(-50,-67,-50,67,paint);canvas.drawLine(48,-67,48,67,paint);
        cap(canvas,-78,upperCap);cap(canvas,67,lowerCap);canvas.restore();
        if(state.phase==TimerState.Phase.RUNNING&&isShown())postInvalidateDelayed(33);
    }
    private void cap(Canvas canvas,float y,Shader shader){fill(0xff547b7c);paint.setShader(shader);canvas.drawRoundRect(-59,y,59,y+11,3,3,paint);paint.setShader(null);stroke(0xff578b89,.8f);canvas.drawRoundRect(-59,y,59,y+11,3,3,paint);stroke(0xb48eebd0,1.1f);canvas.drawLine(-49,y+1.5f,49,y+1.5f,paint);fill(0xff88acaa);canvas.drawCircle(-51,y+5.4f,1,paint);canvas.drawCircle(51,y+5.4f,1,paint);}
    private void fill(int color){paint.setShader(null);paint.setStyle(Paint.Style.FILL);paint.setColor(color);}
    private void stroke(int color,float width){paint.setShader(null);paint.setStyle(Paint.Style.STROKE);paint.setColor(color);paint.setStrokeWidth(width);paint.setStrokeCap(Paint.Cap.ROUND);paint.setStrokeJoin(Paint.Join.ROUND);}
}
