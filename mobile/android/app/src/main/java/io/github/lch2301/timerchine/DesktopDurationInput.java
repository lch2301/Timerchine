package io.github.lch2301.timerchine;

import android.content.Context;
import android.graphics.Color;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.text.method.DigitsKeyListener;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.Locale;
import java.util.function.IntConsumer;

/** The PC's three directly editable countdown fields, with no extra time dialog. */
final class DesktopDurationInput extends LinearLayout {
    final EditText[] parts=new EditText[3];private final DesktopTheme theme;private boolean syncing,dirty,readOnly;private int value;private IntConsumer committed;
    DesktopDurationInput(Context context){super(context);theme=new DesktopTheme(context);setGravity(Gravity.CENTER_VERTICAL);setOrientation(HORIZONTAL);
        for(int i=0;i<3;i++){final int index=i;EditText field=new DurationPart(context);field.setSingleLine(true);field.setText("00");field.setTextSize(28);field.setTypeface(theme.time);field.setTextColor(DesktopTheme.FOREGROUND);field.setGravity(Gravity.CENTER);field.setPadding(0,theme.dp(3),0,theme.dp(3));field.setMinWidth(0);field.setMinimumWidth(0);field.setSelectAllOnFocus(true);field.setRawInputType(InputType.TYPE_CLASS_NUMBER);field.setKeyListener(DigitsKeyListener.getInstance("0123456789:："));field.setImeOptions(i==2?EditorInfo.IME_ACTION_DONE:EditorInfo.IME_ACTION_NEXT);field.setContentDescription(new String[]{"타이머 시간","타이머 분","타이머 초"}[i]);parts[i]=field;addView(field,new LayoutParams(0,-2,1));
            field.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int start,int before,int count){}public void afterTextChanged(Editable text){if(syncing||readOnly)return;dirty=true;String typed=text.toString();String[] pasted=typed.split("[:：]",-1);if(pasted.length==3){syncing=true;for(int p=0;p<3;p++)parts[p].setText(two(parse(pasted[p],p)));syncing=false;}else if(typed.endsWith(":")||typed.endsWith("：")){syncing=true;field.setText(two(parse(typed.substring(0,typed.length()-1),index)));syncing=false;if(index<2)parts[index+1].requestFocus();}else if(text.length()>0){int bounded=parse(typed,index);if(!typed.equals(two(bounded))){syncing=true;field.setText(two(bounded));field.setSelection(field.length());syncing=false;}}}});
            field.setOnFocusChangeListener((v,focused)->{if(!focused){syncing=true;field.setText(two(parse(field.getText().toString(),index)));syncing=false;post(()->{if(!hasFocus())commit();});}});
            field.setOnEditorActionListener((v,action,event)->{if(action==EditorInfo.IME_ACTION_DONE||(event!=null&&event.getKeyCode()==KeyEvent.KEYCODE_ENTER)){commit();clearEditors();return true;}if(action==EditorInfo.IME_ACTION_NEXT&&index<2){parts[index+1].requestFocus();return true;}return false;});
            field.setOnKeyListener((v,key,event)->{if(event.getAction()!=KeyEvent.ACTION_DOWN||readOnly)return false;if(key==KeyEvent.KEYCODE_DPAD_UP||key==KeyEvent.KEYCODE_DPAD_DOWN){field.setText(two(Math.max(0,Math.min(index==0?99:59,parse(field.getText().toString(),index)+(key==KeyEvent.KEYCODE_DPAD_UP?1:-1)))));field.selectAll();return true;}if(key==KeyEvent.KEYCODE_DPAD_RIGHT&&index<2&&field.getSelectionStart()==field.length()&&field.getSelectionEnd()==field.length()){parts[index+1].requestFocus();return true;}if(key==KeyEvent.KEYCODE_DPAD_LEFT&&index>0&&field.getSelectionStart()==0&&field.getSelectionEnd()==0){parts[index-1].requestFocus();return true;}return false;});
            if(i<2){TextView colon=theme.text(":",25,DesktopTheme.FOREGROUND);colon.setTypeface(theme.time);colon.setPadding(theme.dp(1),0,theme.dp(1),0);colon.setOnClickListener(v->{if(!readOnly)parts[index+1].requestFocus();});addView(colon);}
        }setReadOnly(false);
    }
    void onCommit(IntConsumer callback){committed=callback;}
    boolean isEditing(){return hasFocus()||dirty;}
    boolean containsEditor(View target){for(EditText part:parts)if(part==target)return true;return false;}
    void setValue(int seconds){value=seconds;if(!isEditing())display(seconds);}
    void display(int seconds){if(isEditing())return;syncing=true;int[] values={seconds/3600,seconds/60%60,seconds%60};for(int i=0;i<3;i++)parts[i].setText(two(values[i]));syncing=false;}
    void setCompact(boolean compact){for(EditText part:parts)part.setTextSize(compact?22:28);}
    void setReadOnly(boolean locked){readOnly=locked;for(EditText part:parts){part.setFocusable(!locked);part.setFocusableInTouchMode(!locked);part.setCursorVisible(!locked);part.setTextColor(locked?DesktopTheme.FOREGROUND:DesktopTheme.STOP);part.setBackground(locked?null:theme.shape(DesktopTheme.INPUT,3));}}
    void commit(){if(readOnly||!dirty)return;int next=parse(parts[0].getText().toString(),0)*3600+parse(parts[1].getText().toString(),1)*60+parse(parts[2].getText().toString(),2);dirty=false;value=next;syncing=true;int[] values={next/3600,next/60%60,next%60};for(int i=0;i<3;i++)parts[i].setText(two(values[i]));syncing=false;if(committed!=null)committed.accept(next);}
    void clearEditors(){for(EditText part:parts)part.clearFocus();}
    int configuredValue(){return value;}
    private static int parse(String text,int index){try{return(int)Math.max(0,Math.min(index==0?99:59,Long.parseLong(text.trim())));}catch(NumberFormatException invalid){return text.trim().matches("[0-9]+")?(index==0?99:59):0;}}
    private static String two(int value){return String.format(Locale.ROOT,"%02d",value);}

    /** Select the whole component after Android's native caret hit test, even on repeated taps. */
    private static final class DurationPart extends EditText {
        DurationPart(Context context) { super(context); }
        @Override public boolean onTouchEvent(MotionEvent event) {
            boolean handled = super.onTouchEvent(event);
            if (event.getActionMasked() == MotionEvent.ACTION_UP && isFocusable() && isEnabled()) selectAll();
            return handled;
        }
        @Override public boolean performClick() {
            boolean handled = super.performClick();
            if (isFocusable() && isEnabled()) selectAll();
            return handled;
        }
    }
}
