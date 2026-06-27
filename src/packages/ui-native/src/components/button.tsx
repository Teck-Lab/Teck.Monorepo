import * as Slot from "@rn-primitives/slot";
import { type VariantProps, cva } from "class-variance-authority";
import * as React from "react";
import { Pressable, Text } from "react-native";
import { cn } from "../lib/utils";

const buttonVariants = cva("flex flex-row items-center justify-center rounded-md", {
  variants: {
    variant: {
      default: "bg-primary",
      outline: "border border-border bg-background",
    },
    size: { default: "h-10 px-4", sm: "h-9 px-3", lg: "h-11 px-8" },
  },
  defaultVariants: { variant: "default", size: "default" },
});

const buttonTextVariants = cva("text-sm font-medium", {
  variants: {
    variant: {
      default: "text-primary-foreground",
      outline: "text-foreground",
    },
  },
  defaultVariants: { variant: "default" },
});

type ButtonProps = React.ComponentPropsWithoutRef<typeof Pressable> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean };

const Button = React.forwardRef<React.ElementRef<typeof Pressable>, ButtonProps>(
  ({ className, variant, size, asChild = false, children, ...props }, ref) => {
    const Comp = asChild ? Slot.Pressable : Pressable;
    return (
      <Comp ref={ref} className={cn(buttonVariants({ variant, size, className }))} {...props}>
        {typeof children === "string" ? (
          <Text className={cn(buttonTextVariants({ variant }))}>{children}</Text>
        ) : (
          children
        )}
      </Comp>
    );
  },
);
Button.displayName = "Button";

export { Button, buttonVariants, buttonTextVariants };
